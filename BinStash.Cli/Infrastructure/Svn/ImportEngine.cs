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

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BinStash.Cli.Utils;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Core.Chunking;
using Blake3;
using CliFx.Infrastructure;
using Spectre.Console;

namespace BinStash.Cli.Infrastructure.Svn;

public sealed class SvnImportEngine
{
    private readonly SvnCliClient _svn;
    private readonly BinStashApiClient _api;
    private readonly BinStashGrpcClient _ingestClient;
    private readonly ImportStateStore _state;
    private readonly RepositorySummaryDto _repository;
    private readonly IChunker _chunker;
    private readonly int _concurrency;
    private readonly SvnComponentMapper _componentMapper;
    private readonly SvnGlobFilter _globFilter;
    private readonly SvnImportProgress _progress;

    public SvnImportEngine(SvnCliClient svn, BinStashApiClient api, BinStashGrpcClient ingestClient, ImportStateStore state, RepositorySummaryDto repository, IChunker chunker, IConsole console, int concurrency, SvnComponentMapper componentMapper, IEnumerable<string> includes, IEnumerable<string> excludes)
    {
        _svn = svn;
        _api = api;
        _ingestClient = ingestClient;
        _state = state;
        _repository = repository;
        _chunker = chunker;
        _concurrency = Math.Max(1, concurrency);
        _componentMapper = componentMapper;
        _globFilter = new SvnGlobFilter(includes, excludes);
        _progress = new SvnImportProgress(console);
    }

    public async Task RunAsync(string svnRoot, string tenantSlug, bool dryRun, bool resume, int? limit)
    {
        var sourceId = await _state.GetOrCreateSourceAsync(svnRoot, tenantSlug, _repository.Name);

        await _progress.RunStatusAsync($"Scanning tags from {svnRoot} ...", async ctx =>
        {
            var tags = await _svn.ListTagsAsync(svnRoot);
            ctx.Status($"Discovered {tags.Count} tags, updating local state ...");

            foreach (var tag in tags.OrderBy(x => x.CreatedAt))
            {
                var version = MapTagNameToVersion(tag.TagName);
                await _state.UpsertTagAsync(sourceId, tag, version);
            }
        });

        var toImport = await _state.GetTagsToImportAsync(sourceId, limit, resume);
        _progress.Info($"Found {toImport.Count} tag(s) to process.");

        foreach (var tag in toImport)
        {
            try
            {
                await ProcessTagAsync(tag.Id, tag.TagName, tag.TagUrl, tag.Version, tag.Status, dryRun);
            }
            catch (Exception ex)
            {
                await _state.SetTagFailedAsync(tag.Id, ex.ToString());
                _progress.Error($"FAILED {tag.TagName}: {ex.Message}");
            }
        }
    }

    private async Task ProcessTagAsync(long tagId, string tagName, string tagUrl, string version, ImportTagStatus status, bool dryRun)
    {
        _progress.Info($"Processing [tag={tagName}] -> [version={version}]");

        IReadOnlyList<SvnFileEntry> files = [];
        await _progress.RunStatusAsync($"Listing files for {tagName} ...", async ctx =>
        {
            if (status != ImportTagStatus.Discovered)
            {
                _progress.Info($"Tag {tagName} already scanned, loading file list from state ...");
                return;
            }
            
            var rawFiles = await _svn.ListFilesRecursiveAsync(tagUrl);
            files = rawFiles
                .Where(x => _globFilter.ShouldInclude(x.RelativePath))
                .ToList();

            ctx.Status($"Saving {files.Count} file entries ...");
            await _state.SaveTagFilesAsync(tagId, files);
        });

        if (status != ImportTagStatus.Scanned)
            _progress.Info($"{tagName}: {files.Count} files after filtering.");

        if (dryRun)
        {
            _progress.Success($"{tagName}: dry-run completed.");
            return;
        }
        
        SvnLogInfo? tagLog = null;
        await _progress.RunStatusAsync($"Reading SVN log for {tagName} ...", async _ =>
        {
            tagLog = await _svn.GetTagLogAsync(tagUrl);
            _progress.Info($"{tagName}: log message found");
        });

        var tagFiles = await _state.GetTagFilesAsync(tagId);
        
        var releasePackage = new ReleasePackage
        {
            Version = version,
            Notes = string.IsNullOrEmpty(tagLog?.Message) ? $"Imported from SVN tag {tagName}" : tagLog.Message,
            CustomProperties = new Dictionary<string, string>
            {
                ["svn:tag"] = tagName,
                ["svn:url"] = tagUrl
            },
            Components = new List<Component>(),
            Chunks = new List<ChunkInfo>(),
            Stats = new ReleaseStats()
        };

        if (tagLog is not null)
        {
            releasePackage.CustomProperties["svn:revision"] = tagLog!.Revision!.ToString()!;
            releasePackage.CustomProperties["svn:author"] = tagLog!.Author!;
        }

        var componentsByName = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        var fileHashToChunkMap = new ConcurrentDictionary<Hash32, List<ChunkMapEntry>>();
        var fileSizeMap = new ConcurrentDictionary<Hash32, long>();
        var processedFiles =
            new ConcurrentBag<(string ComponentName, ReleaseFile File, List<ChunkMapEntry> ChunkMap, long FileSize)>();

        var cacheHits = 0;
        var cacheMisses = 0;

        using var throttler = new SemaphoreSlim(_concurrency);

        await _progress.RunStatusAsync($"Resolving file hashes and chunk maps for {tagName} ...", async ctx =>
        {
            var completed = 0;
            var total = tagFiles.Count;

            var spoolRoot = Path.Combine(Directory.GetCurrentDirectory(), "svn-import-spool");
            var contentRoot = Path.Combine(spoolRoot, "content");
            var aliasRoot = Path.Combine(spoolRoot, "aliases");
            var stagingRoot = Path.Combine(spoolRoot, "staging");

            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(aliasRoot);
            Directory.CreateDirectory(stagingRoot);

            var canonicalLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            var aliasLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

            await Task.WhenAll(tagFiles.Select(async file =>
            {
                await throttler.WaitAsync();
                try
                {
                    var componentName = _componentMapper.ResolveComponent(file.RelativePath);
                    var releaseFileName = _componentMapper.ResolveReleaseFileName(file.RelativePath, componentName);

                    var candidateKeyHash = HashStringToHex(file.CandidateKey);
                    var aliasFileName = $"{candidateKeyHash}.bin";
                    var aliasPath = Path.Combine(aliasRoot, aliasFileName);

                    var cached = await _state.TryGetCachedFileAsync(file.CandidateKey);
                    if (cached != null)
                    {
                        var fileHash = Hash32.FromHexString(cached.FileHashHex);
                        var canonicalPath = Path.Combine(contentRoot, $"{fileHash.ToHexString()}.bin");

                        var backingPath = File.Exists(aliasPath)
                            ? aliasPath
                            : File.Exists(canonicalPath)
                                ? canonicalPath
                                : null;

                        if (backingPath != null)
                        {
                            if (!File.Exists(aliasPath) && File.Exists(canonicalPath))
                            {
                                var aliasLock = aliasLocks.GetOrAdd(aliasPath, _ => new SemaphoreSlim(1, 1));
                                await aliasLock.WaitAsync();
                                try
                                {
                                    if (!File.Exists(aliasPath) && File.Exists(canonicalPath))
                                        EnsureHardLink(aliasPath, canonicalPath);
                                }
                                finally
                                {
                                    aliasLock.Release();
                                }

                                backingPath = aliasPath;
                            }

                            Interlocked.Increment(ref cacheHits);

                            var cachedMap = JsonSerializer.Deserialize<CachedChunkMap>(cached.ChunkMapJson)
                                            ?? throw new InvalidOperationException("Invalid cached chunk map JSON.");

                            processedFiles.Add((
                                componentName,
                                new ReleaseFile
                                {
                                    Name = releaseFileName,
                                    Hash = fileHash,
                                    Chunks = new List<DeltaChunkRef>()
                                },
                                cachedMap.ToChunkMapEntries(backingPath),
                                cached.FileSize));

                            var done = Interlocked.Increment(ref completed);
                            ctx.Status(
                                $"Resolving files {done}/{total} (cache hits: {cacheHits}, misses: {cacheMisses}) ...");
                            return;
                        }

                        // Cache metadata exists, but spool data is gone -> rebuild it.
                    }

                    Interlocked.Increment(ref cacheMisses);

                    var fullFileUrl = SvnCliClient.BuildEncodedSvnUrl(tagUrl, file.RelativePath);
                    var stagingPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}.bin");

                    try
                    {
                        StreamingFileIngestResult ingestResult;

                        await using (var ingest = new StreamingFileIngest(
                                         stagingPath,
                                         _chunker.CreateStreamingChunker()))
                        {
                            await _svn.PumpFileAsync(
                                fullFileUrl,
                                async (buffer, ct) =>
                                {
                                    await ingest.AppendAsync(buffer, ct);
                                });

                            ingestResult = await ingest.CompleteAsync();
                        }

                        var fileHash = ingestResult.FileHash;
                        var fileSize = ingestResult.FileSize;
                        var canonicalPath = Path.Combine(contentRoot, $"{fileHash.ToHexString()}.bin");

                        var canonicalLock = canonicalLocks.GetOrAdd(canonicalPath, _ => new SemaphoreSlim(1, 1));
                        await canonicalLock.WaitAsync();
                        try
                        {
                            if (!File.Exists(canonicalPath))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);

                                try
                                {
                                    File.Move(stagingPath, canonicalPath);
                                }
                                catch (IOException)
                                {
                                    if (!File.Exists(canonicalPath))
                                        throw;
                                }
                            }
                        }
                        finally
                        {
                            canonicalLock.Release();
                        }

                        if (File.Exists(stagingPath))
                            TryDeleteFile(stagingPath);

                        var aliasLock = aliasLocks.GetOrAdd(aliasPath, _ => new SemaphoreSlim(1, 1));
                        await aliasLock.WaitAsync();
                        try
                        {
                            if (!File.Exists(aliasPath))
                                EnsureHardLink(aliasPath, canonicalPath);
                        }
                        finally
                        {
                            aliasLock.Release();
                        }

                        var chunkMap = RebindChunkMapEntries(ingestResult.ChunkMap, canonicalPath);

                        var cachedMap = ChunkMapCacheExtensions.ToCached(fileHash.ToHexString(), fileSize, chunkMap);
                        var cachedJson = JsonSerializer.Serialize(cachedMap);

                        await _state.SaveCachedFileAsync(new CachedFileResult(
                            file.CandidateKey,
                            fileHash.ToHexString(),
                            fileSize,
                            cachedJson));

                        processedFiles.Add((
                            componentName,
                            new ReleaseFile
                            {
                                Name = releaseFileName,
                                Hash = fileHash,
                                Chunks = new List<DeltaChunkRef>()
                            },
                            chunkMap,
                            fileSize));
                    }
                    catch (SvnPathNotFoundException ex)
                    {
                        _progress.Error($"Skipping missing SVN file: {file.RelativePath}");
                        _progress.Info(ex.Message);
                    }
                    finally
                    {
                        if (File.Exists(stagingPath))
                            TryDeleteFile(stagingPath);
                    }

                    var updated = Interlocked.Increment(ref completed);
                    ctx.Status(
                        $"Resolving files {updated}/{total} (cache hits: {cacheHits}, misses: {cacheMisses}) ...");
                }
                finally
                {
                    throttler.Release();
                }
            }));
        });

        foreach (var group in processedFiles.GroupBy(x => x.ComponentName).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var component = new Component
            {
                Name = group.Key,
                Files = new List<ReleaseFile>()
            };

            foreach (var item in group.OrderBy(x => x.File.Name, StringComparer.Ordinal))
            {
                component.Files.Add(item.File);
                fileHashToChunkMap[item.File.Hash] = item.ChunkMap;
                fileSizeMap[item.File.Hash] = item.FileSize;
            }

            componentsByName[group.Key] = component;
            releasePackage.Components.Add(component);
        }

        _progress.Info(
            $"{tagName}: resolved {processedFiles.Count} files, cache hits={cacheHits}, misses={cacheMisses}");
        
        await _progress.RunStatusAsync($"Checking existing releases for {version} ...", async _ =>
        {
            var existingReleases = await _api.GetReleasesForRepoAsync(_repository.Id);
            if (existingReleases is { Count:>0 } && existingReleases.Any(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Release with version {version} already exists.");
        });
        
        var ingestSessionId = Guid.Empty;
        await _progress.RunStatusAsync($"Creating ingest session for {version} ...",
            async _ => { ingestSessionId = await _api.CreateIngestSessionAsync(_repository.Id, version); });

        List<Hash32> missingFileChecksums = [];
        await _progress.RunStatusAsync($"Checking missing file definitions for {version} ...", async _ =>
        {
            var fileHashes = fileHashToChunkMap.Keys.ToList();
            missingFileChecksums = await _api.GetMissingFileChecksumsAsync(_repository.Id, ingestSessionId, fileHashes);
        });

        var missingFileMap = fileHashToChunkMap
            .Where(kvp => missingFileChecksums.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _progress.Info($"{tagName}: missing file definitions = {missingFileChecksums.Count}");

        var uniqueChunkChecksums = missingFileMap.Values
            .SelectMany(x => x.Select(e => e.Checksum))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        List<Hash32> missingChunkChecksums = [];
        if (uniqueChunkChecksums.Count > 0)
        {
            await _progress.RunStatusAsync($"Checking missing chunks for {version} ...",
                async _ =>
                {
                    missingChunkChecksums =
                        await _api.GetMissingChunkChecksumsAsync(_repository.Id, ingestSessionId, uniqueChunkChecksums);
                });
        }

        _progress.Info($"{tagName}: missing chunks = {missingChunkChecksums.Count}");

        if (missingChunkChecksums.Count > 0)
        {
            var missingChunkSet = new HashSet<Hash32>(missingChunkChecksums);

            var selectedEntries = missingFileMap.Values
                .SelectMany(x => x)
                .Where(x => missingChunkSet.Contains(x.Checksum))
                .GroupBy(x => x.Checksum)
                .Select(g => g.First())
                .ToList();

            await _progress.RunStatusAsync($"Uploading {selectedEntries.Count} chunks for {version} ...", async ctx =>
            {
                // TODO: Fallback to rest if grpc upload fails (e.g. due to large batch size or network issues)
                await _ingestClient.UploadChunksAsync(
                    _repository.Id,
                    ingestSessionId,
                    _chunker,
                    selectedEntries,
                    progressCallback: (uploaded, total) =>
                    {
                        ctx.Status($"Uploading chunks {uploaded}/{total} ...");
                        return Task.CompletedTask;
                    });
                /*await _api.UploadChunksAsync(
                    _repository.Id,
                    ingestSessionId,
                    _chunker,
                    selectedEntries,
                    progressCallback: (uploaded, total) =>
                    {
                        ctx.Status($"Uploading chunks {uploaded}/{total} ...");
                        return Task.CompletedTask;
                    });*/
            });
        }

        if (missingFileChecksums.Count > 0)
        {
            var fileDefs = missingFileChecksums.ToDictionary(
                fileHash => fileHash,
                fileHash => (
                    Chunks: fileHashToChunkMap[fileHash].Select(x => x.Checksum).ToList(),
                    Length: fileSizeMap[fileHash]
                ));

            await _progress.RunStatusAsync($"Uploading {fileDefs.Count} file definitions for {version} ...",
                async ctx =>
                {
                    // TODO: Fallback to rest if grpc upload fails (e.g. due to large batch size or network issues)
                    await _ingestClient.UploadFileDefinitionsAsync(
                        _repository.Id,
                        ingestSessionId,
                        fileDefs,
                        progressCallback: (uploaded, total) =>
                        {
                            ctx.Status($"Uploading file definitions {uploaded}/{total} ...");
                            return Task.CompletedTask;
                        });
                    /*await _api.UploadFileDefinitionsAsync(
                        _repository.Id,
                        ingestSessionId,
                        fileDefs,
                        progressCallback: (uploaded, total) =>
                        {
                            ctx.Status($"Uploading file definitions {uploaded}/{total} ...");
                            return Task.CompletedTask;
                        });*/
                });
        }

        var allChunkChecksums = fileHashToChunkMap.Values
            .SelectMany(x => x.Select(e => e.Checksum))
            .Distinct()
            .ToList();

        releasePackage.Stats = new ReleaseStats
        {
            ComponentCount = (uint)releasePackage.Components.Count,
            FileCount = (uint)releasePackage.Components.Sum(x => x.Files.Count),
            ChunkCount = (uint)allChunkChecksums.Count,
            RawSize = (ulong)fileSizeMap.Values.Sum(),
            DedupedSize = (ulong)fileHashToChunkMap.Values.SelectMany(x => x).Sum(x => x.Length)
        };

        await _progress.RunStatusAsync($"Finalizing release {version} ...",
            async _ => { await _api.CreateReleaseAsync(ingestSessionId, _repository.Id.ToString(), releasePackage); });

        await _state.SetTagImportedAsync(tagId, version);
        _progress.Success($"{tagName} imported as {version}");
    }
    
    private static string MapTagNameToVersion(string tagName)
    {
        var match = Regex.Match(tagName, @"-(\d+)/?$");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract version from tag: {tagName}");

        return $"1.0.{match.Groups[1].Value}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static string HashStringToHex(string value)
        => new Hash32(Hasher.Hash(Encoding.UTF8.GetBytes(value)).AsSpan()).ToHexString();

    private static List<ChunkMapEntry> RebindChunkMapEntries(
        List<ChunkBoundary> boundaries,
        string filePath)
    {
        return boundaries
            .Select(x => new ChunkMapEntry
            {
                 FilePath = filePath,
                 Offset = x.Offset,
                 Length = x.Length,
                 Checksum = x.Checksum
             })
            .ToList();
    }
    
    static void EnsureHardLink(string linkPath, string existingFilePath)
    {
        if (File.Exists(linkPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        try
        {
            HardLinkHelper.CreateHardLink(linkPath, existingFilePath);
        }
        catch (IOException)
        {
            // Another worker may have created it concurrently.
            if (!File.Exists(linkPath))
                throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }
}