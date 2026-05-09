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

using System.Diagnostics;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage.FileDefinition;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Services.ChunkStores;

public sealed class ChunkStoreStatsCollector
{
    private readonly BinStashDbContext _db;
    private readonly IChunkStoreService _chunkStoreService;
    private readonly ILogger<ChunkStoreStatsCollector> _logger;

    public ChunkStoreStatsCollector(BinStashDbContext db, IChunkStoreService chunkStoreService, ILogger<ChunkStoreStatsCollector> logger)
    {
        _db = db;
        _chunkStoreService = chunkStoreService;
        _logger = logger;
    }

    public async Task<ChunkStoreStatsSnapshot> CollectAndStoreAsync(Guid chunkStoreId, CancellationToken cancellationToken = default)
    {
        var store = await _db.ChunkStores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == chunkStoreId, cancellationToken);

        if (store == null)
            throw new InvalidOperationException($"Chunk store {chunkStoreId} not found.");

        _logger.LogInformation("Starting stats collection for chunk store '{StoreName}' ({StoreId})", store.Name, chunkStoreId);
        var totalSw = Stopwatch.StartNew();

        var result = await CollectAsync(store, cancellationToken);

        var snapshot = new ChunkStoreStatsSnapshot
        {
            Id = Guid.CreateVersion7(),
            ChunkStoreId = store.Id,
            CollectedAt = DateTimeOffset.UtcNow,

            ChunkCount = result.ChunkCount,
            FileDefinitionCount = result.FileDefinitionCount,
            ReleaseCount = result.ReleaseCount,

            ChunkPackBytes = result.ChunkPackBytes,
            FileDefinitionPackBytes = result.FileDefinitionPackBytes,
            ReleasePackageBytes = result.ReleasePackageBytes,
            IndexBytes = result.IndexBytes,
            PhysicalBytesTotal = result.PhysicalBytesTotal,

            TotalLogicalBytes = result.TotalLogicalBytes,
            UniqueFileBytes = result.UniqueFileBytes,
            UniqueLogicalChunkBytes = result.UniqueLogicalChunkBytes,
            UniqueCompressedChunkBytes = result.UniqueCompressedChunkBytes,

            ReferencedUniqueChunkBytes = result.ReferencedUniqueChunkBytes,

            CompressionRatio = result.CompressionRatio,
            DeduplicationRatio = result.DeduplicationRatio,
            EffectiveStorageRatio = result.EffectiveStorageRatio,

            CompressionSavedBytes = result.CompressionSavedBytes,
            DeduplicationSavedBytes = result.DeduplicationSavedBytes,

            ChunkPackFileCount = result.ChunkPackFileCount,
            FileDefinitionPackFileCount = result.FileDefinitionPackFileCount,
            ReleasePackageFileCount = result.ReleasePackageFileCount,
            IndexFileCount = result.IndexFileCount,

            VolumeTotalBytes = result.VolumeTotalBytes,
            VolumeFreeBytes = result.VolumeFreeBytes,

            AvgChunkSize = result.AvgChunkSize,
            AvgCompressedChunkSize = result.AvgCompressedChunkSize
        };

        _db.ChunkStoreStatsSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stats collection complete for '{StoreName}' in {Elapsed}ms — " +
            "chunks: {ChunkCount}, files: {FileDefinitionCount}, releases: {ReleaseCount}, " +
            "physical: {PhysicalMB:F1} MB, logical: {LogicalMB:F1} MB",
            store.Name, totalSw.ElapsedMilliseconds,
            result.ChunkCount, result.FileDefinitionCount, result.ReleaseCount,
            result.PhysicalBytesTotal / 1_048_576.0, result.TotalLogicalBytes / 1_048_576.0);

        return snapshot;
    }

    private async Task<ChunkStoreStatsResult> CollectAsync(ChunkStore store, CancellationToken cancellationToken = default)
    {
        var chunkStoreId = store.Id;
        var sw = Stopwatch.StartNew();

        _logger.LogDebug("[{StoreName}] Querying chunk DB stats", store.Name);
        var chunkDbStats = await _db.Chunks
            .AsNoTracking()
            .Where(x => x.ChunkStoreId == chunkStoreId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = (long)g.LongCount(),
                LogicalBytes = (long?)g.Sum(x => (long)x.Length) ?? 0,
                CompressedBytes = (long?)g.Sum(x => (long)x.CompressedLength) ?? 0,
                AvgLength = g.Any() ? (long)g.Average(x => x.Length) : 0,
                AvgCompressedLength = g.Any() ? (long)g.Average(x => x.CompressedLength) : 0
            })
            .FirstOrDefaultAsync(cancellationToken);
        _logger.LogDebug("[{StoreName}] Chunk DB stats done in {Elapsed}ms — {Count} chunks", store.Name, sw.ElapsedMilliseconds, chunkDbStats?.Count ?? 0);

        sw.Restart();
        _logger.LogDebug("[{StoreName}] Querying file definition count", store.Name);
        var fileDefinitionCount = await _db.FileDefinitions
            .AsNoTracking()
            .LongCountAsync(x => x.ChunkStoreId == chunkStoreId, cancellationToken);
        _logger.LogDebug("[{StoreName}] File definition count done in {Elapsed}ms — {Count} file definitions", store.Name, sw.ElapsedMilliseconds, fileDefinitionCount);

        sw.Restart();
        _logger.LogDebug("[{StoreName}] Querying release count", store.Name);
        var releaseCount = await _db.Repositories
            .AsNoTracking()
            .Where(r => r.ChunkStoreId == chunkStoreId)
            .SelectMany(r => r.Releases)
            .LongCountAsync(cancellationToken);
        _logger.LogDebug("[{StoreName}] Release count done in {Elapsed}ms — {Count} releases", store.Name, sw.ElapsedMilliseconds, releaseCount);

        sw.Restart();
        _logger.LogDebug("[{StoreName}] Collecting physical stats", store.Name);
        var physicalStats = await _chunkStoreService.GetPhysicalStatsAsync(store);
        _logger.LogDebug("[{StoreName}] Physical stats done in {Elapsed}ms — {PhysicalMB:F1} MB total", store.Name, sw.ElapsedMilliseconds, physicalStats.PhysicalBytesTotal / 1_048_576.0);

        sw.Restart();
        _logger.LogInformation("[{StoreName}] Collecting logical stats (may take a while for large stores)", store.Name);
        var logicalStats = await CollectLogicalStatsAsync(store, cancellationToken);
        _logger.LogInformation("[{StoreName}] Logical stats done in {Elapsed}ms", store.Name, sw.ElapsedMilliseconds);

        var uniqueLogicalChunkBytes = chunkDbStats?.LogicalBytes ?? 0;
        var uniqueCompressedChunkBytes = chunkDbStats?.CompressedBytes ?? 0;

        var totalLogicalBytes = logicalStats.TotalLogicalBytes;
        var uniqueFileBytes = logicalStats.UniqueFileBytes;
        var referencedUniqueChunkBytes = logicalStats.ReferencedUniqueChunkBytes;

        var compressionRatio = uniqueCompressedChunkBytes > 0
            ? (double)uniqueLogicalChunkBytes / uniqueCompressedChunkBytes
            : 1.0;

        var deduplicationRatio = uniqueLogicalChunkBytes > 0
            ? (double)totalLogicalBytes / uniqueLogicalChunkBytes
            : 1.0;

        var effectiveStorageRatio = physicalStats.PhysicalBytesTotal > 0
            ? (double)totalLogicalBytes / physicalStats.PhysicalBytesTotal
            : 1.0;

        return new ChunkStoreStatsResult
        {
            ChunkCount = chunkDbStats?.Count ?? 0,
            FileDefinitionCount = fileDefinitionCount,
            ReleaseCount = releaseCount,

            ChunkPackBytes = physicalStats.ChunkPackBytes,
            FileDefinitionPackBytes = physicalStats.FileDefinitionPackBytes,
            ReleasePackageBytes = physicalStats.ReleasePackageBytes,
            IndexBytes = physicalStats.IndexBytes,
            PhysicalBytesTotal = physicalStats.PhysicalBytesTotal,

            TotalLogicalBytes = totalLogicalBytes,
            UniqueFileBytes = uniqueFileBytes,
            UniqueLogicalChunkBytes = uniqueLogicalChunkBytes,
            UniqueCompressedChunkBytes = uniqueCompressedChunkBytes,
            ReferencedUniqueChunkBytes = referencedUniqueChunkBytes,

            CompressionRatio = compressionRatio,
            DeduplicationRatio = deduplicationRatio,
            EffectiveStorageRatio = effectiveStorageRatio,

            CompressionSavedBytes = Math.Max(0, uniqueLogicalChunkBytes - uniqueCompressedChunkBytes),
            DeduplicationSavedBytes = Math.Max(0, totalLogicalBytes - uniqueLogicalChunkBytes),

            ChunkPackFileCount = physicalStats.ChunkPackFileCount,
            FileDefinitionPackFileCount = physicalStats.FileDefinitionPackFileCount,
            ReleasePackageFileCount = physicalStats.ReleasePackageFileCount,
            IndexFileCount = physicalStats.IndexFileCount,

            VolumeTotalBytes = physicalStats.VolumeTotalBytes,
            VolumeFreeBytes = physicalStats.VolumeFreeBytes,

            AvgChunkSize = chunkDbStats?.AvgLength ?? 0,
            AvgCompressedChunkSize = chunkDbStats?.AvgCompressedLength ?? 0
        };
    }

    private async Task<LogicalStats> CollectLogicalStatsAsync(ChunkStore store, CancellationToken cancellationToken)
    {
        var chunkStoreId = store.Id;
        var sw = Stopwatch.StartNew();

        _logger.LogDebug("[{StoreName}] Loading releases", store.Name);
        var releases = await _db.Releases
            .AsNoTracking()
            .Join(
                _db.Repositories.AsNoTracking(),
                release => release.RepoId,
                repo => repo.Id,
                (release, repo) => new { Release = release, Repo = repo })
            .Where(x => x.Repo.ChunkStoreId == chunkStoreId)
            .Select(x => x.Release)
            .ToListAsync(cancellationToken);
        _logger.LogDebug("[{StoreName}] Loaded {ReleaseCount} releases in {Elapsed}ms", store.Name, releases.Count, sw.ElapsedMilliseconds);

        if (releases.Count == 0)
        {
            return new LogicalStats
            {
                TotalLogicalBytes = 0,
                UniqueFileBytes = 0,
                ReferencedUniqueChunkBytes = 0
            };
        }

        var packageIds = releases
            .Select(r => r.ReleaseDefinitionChecksum.ToHexString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        sw.Restart();
        _logger.LogDebug("[{StoreName}] Retrieving {PackageCount} release packages from store", store.Name, packageIds.Length);
        var packageMap = await _chunkStoreService.RetrieveReleasePackagesAsync(store, packageIds);
        _logger.LogDebug("[{StoreName}] Retrieved {RetrievedCount}/{PackageCount} release packages in {Elapsed}ms", store.Name, packageMap.Count, packageIds.Length, sw.ElapsedMilliseconds);

        long totalLogicalBytes = 0;

        // Unique stored content hashes referenced by all output artifacts across all releases
        var uniqueContentHashes = new HashSet<Hash32>();

        sw.Restart();
        _logger.LogDebug("[{StoreName}] Processing {ReleaseCount} releases to compute logical sizes", store.Name, releases.Count);
        var processedReleases = 0;

        foreach (var release in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packageId = release.ReleaseDefinitionChecksum.ToHexString();
            if (!packageMap.TryGetValue(packageId, out var packageData))
            {
                processedReleases++;
                continue;
            }

            var (package, _) = await ReleasePackageSerializer.DeserializeAsync(packageData, cancellationToken);
            var outputArtifacts = package.OutputArtifacts ?? [];

            if (outputArtifacts.Count == 0)
            {
                processedReleases++;
                continue;
            }

            var artifactContentHashes = CollectReferencedContentHashes(outputArtifacts);

            Dictionary<Hash32, long> fileLengthsInRelease = [];
            if (artifactContentHashes.Count > 0)
            {
                fileLengthsInRelease = await FetchInBatchesAsync(
                    artifactContentHashes,
                    batch => _db.FileDefinitions
                        .AsNoTracking()
                        .Where(x => x.ChunkStoreId == chunkStoreId && batch.Contains(x.Checksum))
                        .Select(x => new ChecksumLength(x.Checksum, x.Length))
                        .ToListAsync(cancellationToken),
                    cancellationToken);
            }

            foreach (var artifact in outputArtifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                totalLogicalBytes += CalculateLogicalArtifactSize(artifact, fileLengthsInRelease);

                foreach (var hash in CollectReferencedContentHashes([artifact]))
                    uniqueContentHashes.Add(hash);
            }

            processedReleases++;
            if (processedReleases % 100 == 0 || processedReleases == releases.Count)
            {
                _logger.LogDebug(
                    "[{StoreName}] Release processing progress: {Processed}/{Total} releases, {UniqueFiles} unique files so far ({Elapsed}ms elapsed)",
                    store.Name, processedReleases, releases.Count, uniqueContentHashes.Count, sw.ElapsedMilliseconds);
            }
        }

        _logger.LogDebug("[{StoreName}] Release processing done in {Elapsed}ms — {UniqueFiles} unique content hashes", store.Name, sw.ElapsedMilliseconds, uniqueContentHashes.Count);

        sw.Restart();
        long uniqueFileBytes = 0;
        if (uniqueContentHashes.Count > 0)
        {
            _logger.LogDebug("[{StoreName}] Summing unique file bytes for {Count} content hashes (in batches of {BatchSize})", store.Name, uniqueContentHashes.Count, HashBatchSize);
            uniqueFileBytes = await SumInBatchesAsync(
                uniqueContentHashes,
                batch => _db.FileDefinitions
                    .AsNoTracking()
                    .Where(x => x.ChunkStoreId == chunkStoreId && batch.Contains(x.Checksum))
                    .SumAsync(x => (long?)x.Length, cancellationToken),
                cancellationToken);
            _logger.LogDebug("[{StoreName}] Unique file bytes: {UniqueFileMB:F1} MB (done in {Elapsed}ms)", store.Name, uniqueFileBytes / 1_048_576.0, sw.ElapsedMilliseconds);
        }

        var fileHashStrings = uniqueContentHashes
            .Select(x => x.ToHexString())
            .ToArray();

        sw.Restart();
        _logger.LogDebug("[{StoreName}] Retrieving {Count} file definition blobs from store", store.Name, fileHashStrings.Length);
        var fileDefinitionMap = fileHashStrings.Length > 0
            ? await _chunkStoreService.RetrieveFileDefinitionsAsync(store, fileHashStrings)
            : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        _logger.LogDebug("[{StoreName}] Retrieved {Count} file definition blobs in {Elapsed}ms", store.Name, fileDefinitionMap.Count, sw.ElapsedMilliseconds);

        sw.Restart();
        _logger.LogDebug("[{StoreName}] Deserialising file definition blobs to collect chunk hashes", store.Name);
        var referencedUniqueChunks = new HashSet<Hash32>();

        foreach (var fileDefinitionData in fileDefinitionMap.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = FileDefinitionRecord.Deserialize(fileDefinitionData);
            foreach (var chunkHash in record.ChunkHashes)
                referencedUniqueChunks.Add(chunkHash);
        }
        _logger.LogDebug("[{StoreName}] Collected {UniqueChunks} referenced unique chunk hashes in {Elapsed}ms", store.Name, referencedUniqueChunks.Count, sw.ElapsedMilliseconds);

        sw.Restart();
        long referencedUniqueChunkBytes = 0;
        if (referencedUniqueChunks.Count > 0)
        {
            _logger.LogDebug("[{StoreName}] Summing referenced chunk bytes for {Count} chunk hashes (in batches of {BatchSize})", store.Name, referencedUniqueChunks.Count, HashBatchSize);
            referencedUniqueChunkBytes = await SumInBatchesAsync(
                referencedUniqueChunks,
                batch => _db.Chunks
                    .AsNoTracking()
                    .Where(x => x.ChunkStoreId == chunkStoreId && batch.Contains(x.Checksum))
                    .SumAsync(x => (long?)x.Length, cancellationToken),
                cancellationToken);
            _logger.LogDebug("[{StoreName}] Referenced unique chunk bytes: {ChunkMB:F1} MB (done in {Elapsed}ms)", store.Name, referencedUniqueChunkBytes / 1_048_576.0, sw.ElapsedMilliseconds);
        }

        return new LogicalStats
        {
            TotalLogicalBytes = totalLogicalBytes,
            UniqueFileBytes = uniqueFileBytes,
            ReferencedUniqueChunkBytes = referencedUniqueChunkBytes
        };
    }

    private static List<Hash32> CollectReferencedContentHashes(IEnumerable<OutputArtifact> outputArtifacts)
    {
        var hashes = new List<Hash32>();

        foreach (var artifact in outputArtifacts)
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking opaque:
                    if (opaque.ContentHash != null)
                        hashes.Add(opaque.ContentHash.Value);
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.ContentHash != null)
                            hashes.Add(member.ContentHash.Value);
                    }
                    break;
            }
        }

        return hashes;
    }

    private static long CalculateLogicalArtifactSize(OutputArtifact artifact, IReadOnlyDictionary<Hash32, long> fileLengths)
    {
        return artifact.Backing switch
        {
            OpaqueBlobBacking opaque => CalculateOpaqueArtifactSize(opaque, fileLengths),
            ReconstructedContainerBacking reconstructed => CalculateReconstructedArtifactSize(reconstructed, fileLengths),
            _ => 0L
        };
    }

    private static long CalculateOpaqueArtifactSize(OpaqueBlobBacking backing, IReadOnlyDictionary<Hash32, long> fileLengths)
    {
        if (backing.Length.HasValue)
            return backing.Length.Value;

        if (backing.ContentHash != null && fileLengths.TryGetValue(backing.ContentHash.Value, out var len))
            return len;

        return 0L;
    }

    private static long CalculateReconstructedArtifactSize(ReconstructedContainerBacking backing, IReadOnlyDictionary<Hash32, long> fileLengths)
    {
        long total = 0;

        foreach (var member in backing.Members)
        {
            if (member.Length.HasValue)
            {
                total += member.Length.Value;
                continue;
            }

            if (member.ContentHash != null && fileLengths.TryGetValue(member.ContentHash.Value, out var len))
                total += len;
        }

        return total;
    }
    
    public sealed class ChunkStoreStatsResult
    {
        public long ChunkCount { get; set; }
        public long FileDefinitionCount { get; set; }
        public long ReleaseCount { get; set; }

        public long ChunkPackBytes { get; set; }
        public long FileDefinitionPackBytes { get; set; }
        public long ReleasePackageBytes { get; set; }
        public long IndexBytes { get; set; }
        public long PhysicalBytesTotal { get; set; }

        public long TotalLogicalBytes { get; set; }
        public long UniqueFileBytes { get; set; }
        public long UniqueLogicalChunkBytes { get; set; }
        public long UniqueCompressedChunkBytes { get; set; }
        public long ReferencedUniqueChunkBytes { get; set; }

        public double CompressionRatio { get; set; }
        public double DeduplicationRatio { get; set; }
        public double EffectiveStorageRatio { get; set; }

        public long CompressionSavedBytes { get; set; }
        public long DeduplicationSavedBytes { get; set; }

        public int ChunkPackFileCount { get; set; }
        public int FileDefinitionPackFileCount { get; set; }
        public int ReleasePackageFileCount { get; set; }
        public int IndexFileCount { get; set; }

        public long VolumeTotalBytes { get; set; }
        public long VolumeFreeBytes { get; set; }

        public long AvgChunkSize { get; set; }
        public long AvgCompressedChunkSize { get; set; }
    }

    /// <summary>
    /// Maximum number of hash values sent in a single <c>= ANY(...)</c> array parameter.
    /// Keeping batches small avoids PostgreSQL shared-memory exhaustion when the set is large.
    /// </summary>
    private const int HashBatchSize = 1000;

    /// <summary>
    /// Sums a long? column over a potentially large set of hash keys by splitting
    /// the set into batches of at most <see cref="HashBatchSize"/> and accumulating results.
    /// </summary>
    private static async Task<long> SumInBatchesAsync(
        IEnumerable<Hash32> hashes,
        Func<List<Hash32>, Task<long?>> sumQuery,
        CancellationToken cancellationToken)
    {
        long total = 0;
        var batch = new List<Hash32>(HashBatchSize);

        foreach (var hash in hashes)
        {
            batch.Add(hash);
            if (batch.Count < HashBatchSize)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            total += await sumQuery(batch) ?? 0;
            batch.Clear();
        }

        if (batch.Count > 0)
            total += await sumQuery(batch) ?? 0;

        return total;
    }

    /// <summary>
    /// Fetches a Hash32 → long dictionary over a potentially large set of keys by splitting
    /// into batches of at most <see cref="HashBatchSize"/> and merging results.
    /// </summary>
    private static async Task<Dictionary<Hash32, long>> FetchInBatchesAsync(
        IEnumerable<Hash32> hashes,
        Func<List<Hash32>, Task<List<ChecksumLength>>> fetchQuery,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Hash32, long>();
        var batch = new List<Hash32>(HashBatchSize);

        foreach (var hash in hashes)
        {
            batch.Add(hash);
            if (batch.Count < HashBatchSize)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in await fetchQuery(batch))
                result.TryAdd(item.Checksum, item.Length);
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            foreach (var item in await fetchQuery(batch))
                result.TryAdd(item.Checksum, item.Length);
        }

        return result;
    }

    // Intermediate projection type used to avoid ValueTuple materialisation issues with Npgsql.
    private sealed record ChecksumLength(Hash32 Checksum, long Length);

    private sealed class LogicalStats
    {
        public long TotalLogicalBytes { get; set; }
        public long UniqueFileBytes { get; set; }
        public long ReferencedUniqueChunkBytes { get; set; }
    }
}