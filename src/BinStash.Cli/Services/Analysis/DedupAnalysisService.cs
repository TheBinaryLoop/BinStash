// Copyright (C) 2025-2026  Lukas Eßmann
//
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
//
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
//
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Cli.Clients;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Chunking;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Models;
using ZstdNet;

namespace BinStash.Cli.Services.Analysis;

/// <summary>
/// Answers "what would this cost to upload" without uploading it.
/// </summary>
/// <remarks>
/// Runs the same discovery, ingestion and chunking the real upload runs — anything less would
/// predict a number the upload then fails to match. What it deliberately does not do is open an
/// ingest session: the prediction is most useful to a workspace deciding whether it can afford the
/// upload, and session creation is exactly where an over-quota workspace is turned away.
/// </remarks>
public sealed class DedupAnalysisService
{
    private readonly IInputDiscoveryService _inputDiscoveryService;
    private readonly IReleaseIngestionEngine _releaseIngestionEngine;
    private readonly IContentProcessor _contentProcessor;

    public DedupAnalysisService(IInputDiscoveryService inputDiscoveryService, IReleaseIngestionEngine releaseIngestionEngine, IContentProcessor contentProcessor)
    {
        _inputDiscoveryService = inputDiscoveryService;
        _releaseIngestionEngine = releaseIngestionEngine;
        _contentProcessor = contentProcessor;
    }

    public async Task<DedupAnalysisResult> AnalyzeAsync(DedupAnalysisRequest request, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var componentMap = BuildComponentMap(request.TargetPath);
        var inputs = DiscoverInputs(request.TargetPath, componentMap);

        if (inputs.Count == 0)
            throw new InvalidOperationException($"Nothing to analyze at '{request.TargetPath}'.");

        progress?.Report($"Scanning {inputs.Count:N0} file(s)");

        var ingestionResult = await _releaseIngestionEngine.IngestAsync(inputs, componentMap, ct);
        var hashingResult = _contentProcessor.HashStorageWorkItems(ingestionResult);

        progress?.Report("Chunking");

        // Every piece of content is chunked, not just the ones the store is missing. The store is
        // asked about chunks, and a file it already holds whole still contributes its chunks to
        // the totals — otherwise "stored" and "new" would be measured over different populations.
        var allContentHashes = new HashSet<Hash32>(hashingResult.ContentHashes.Keys);
        var chunkMapResult = _contentProcessor.GenerateChunkMaps(hashingResult, ingestionResult, request.Chunker, allContentHashes);

        // Deduplicated within the upload itself before the store is consulted: a chunk repeated
        // across files is uploaded once, so counting it twice would overstate the cost.
        var uniqueChunks = new Dictionary<Hash32, ChunkMapEntry>();
        foreach (var entry in chunkMapResult.FileChunkMaps.Values.SelectMany(x => x))
            uniqueChunks.TryAdd(entry.Checksum, entry);

        var totalLogicalBytes = hashingResult.ContentSizes.Values.Sum();

        progress?.Report($"Querying the store for {uniqueChunks.Count:N0} chunk(s)");

        var missing = uniqueChunks.Count == 0
            ? []
            : await request.Client.GetMissingChunkChecksumsForAnalysisAsync(request.TenantId, request.RepositoryId, uniqueChunks.Keys.ToList());

        var missingSet = new HashSet<Hash32>(missing);
        var newEntries = uniqueChunks.Where(x => missingSet.Contains(x.Key)).Select(x => x.Value).ToList();
        var uploadBytes = newEntries.Sum(x => (long)x.Length);

        progress?.Report("Estimating compressed size");

        var compression = await EstimateCompressedSizeAsync(request.Chunker, newEntries, uploadBytes, request.CompressionSampleChunks, ct);

        return new DedupAnalysisResult(
            FileCount: inputs.Count,
            TotalLogicalBytes: totalLogicalBytes,
            TotalChunks: uniqueChunks.Count,
            StoredChunks: uniqueChunks.Count - newEntries.Count,
            NewChunks: newEntries.Count,
            UploadBytes: uploadBytes,
            EstimatedPackBytes: compression.EstimatedBytes,
            CompressionSampled: compression.Sampled);
    }

    /// <summary>
    /// Compresses the new chunks to find out how much space they will actually occupy.
    /// </summary>
    /// <remarks>
    /// Sampled rather than exhaustive by default. Compressing every chunk of a multi-gigabyte
    /// upload costs about as much CPU as the upload itself, which is a poor trade for a figure
    /// whose job is to inform a decision. The sample is taken across the chunk list rather than
    /// from its head, because chunk order follows file order and the first megabytes of a build
    /// output are rarely representative of the rest.
    /// </remarks>
    private static async Task<(long EstimatedBytes, bool Sampled)> EstimateCompressedSizeAsync(IChunker chunker, IReadOnlyList<ChunkMapEntry> newChunks, long uploadBytes, int sampleSize, CancellationToken ct)
    {
        if (newChunks.Count == 0 || uploadBytes == 0)
            return (0, false);

        var exhaustive = sampleSize <= 0 || sampleSize >= newChunks.Count;
        var stride = exhaustive ? 1 : (int)Math.Max(1, newChunks.Count / (double)sampleSize);

        long sampledRaw = 0;
        long sampledCompressed = 0;

        using var compressor = new Compressor();
        for (var i = 0; i < newChunks.Count; i += stride)
        {
            ct.ThrowIfCancellationRequested();

            var data = (await chunker.LoadChunkDataAsync(newChunks[i], ct)).Data;
            sampledRaw += data.Length;
            sampledCompressed += compressor.Wrap(data).Length;
        }

        if (sampledRaw == 0)
            return (0, !exhaustive);

        if (exhaustive)
            return (sampledCompressed, false);

        var ratio = sampledCompressed / (double)sampledRaw;
        return ((long)Math.Round(uploadBytes * ratio), true);
    }

    /// <summary>
    /// A single file is analyzed as a one-file component rooted at its directory, so that a
    /// sibling in the same folder is not swept in. A directory is analyzed whole.
    /// </summary>
    private IReadOnlyList<InputItem> DiscoverInputs(string targetPath, Dictionary<string, Component> componentMap)
    {
        if (Directory.Exists(targetPath))
            return _inputDiscoveryService.DiscoverFiles(targetPath, componentMap);

        var info = new FileInfo(targetPath);
        if (!info.Exists)
            throw new FileNotFoundException($"'{targetPath}' does not exist.", targetPath);

        var component = componentMap.Values.First();
        return
        [
            new InputItem(
                AbsolutePath: info.FullName,
                RelativePath: info.Name,
                RelativePathWithinComponent: info.Name,
                Component: component,
                Length: info.Length,
                LastWriteTimeUtc: info.LastWriteTimeUtc)
        ];
    }

    private static Dictionary<string, Component> BuildComponentMap(string targetPath)
    {
        var name = Directory.Exists(targetPath)
            ? new DirectoryInfo(targetPath).Name
            : Path.GetFileNameWithoutExtension(targetPath);

        return new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = new() { Name = string.IsNullOrWhiteSpace(name) ? "root" : name, Files = [] }
        };
    }
}

public sealed record DedupAnalysisRequest(BinStashApiClient Client, Guid TenantId, Guid RepositoryId, string TargetPath, IChunker Chunker, int CompressionSampleChunks);

public sealed record DedupAnalysisResult(int FileCount, long TotalLogicalBytes, int TotalChunks, int StoredChunks, int NewChunks, long UploadBytes, long EstimatedPackBytes, bool CompressionSampled)
{
    public double StoredFraction => TotalChunks == 0 ? 0 : StoredChunks / (double)TotalChunks;

    public double NewFraction => TotalChunks == 0 ? 0 : NewChunks / (double)TotalChunks;

    /// <summary>How much of the raw input never crosses the wire, as a share of the whole.</summary>
    public double DeduplicationSaving => TotalLogicalBytes == 0 ? 0 : 1 - UploadBytes / (double)TotalLogicalBytes;
}
