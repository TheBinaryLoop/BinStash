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
using BinStash.Cli.Services.Releases;
using BinStash.Cli.Utils;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Core.Chunking;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Execution;
using BinStash.Core.Ingestion.Models;
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
    private readonly IReleaseIngestionEngine _releaseIngestionEngine;
    private readonly IContentProcessor _contentProcessor;
    private readonly ServerUploadPlanner _serverUploadPlanner;
    private readonly ReleasePackageBuilder _releasePackageBuilder;

    public SvnImportEngine(SvnCliClient svn, BinStashApiClient api, BinStashGrpcClient ingestClient, ImportStateStore state, RepositorySummaryDto repository, IChunker chunker, IConsole console, int concurrency, SvnComponentMapper componentMapper, IEnumerable<string> includes, IEnumerable<string> excludes, IReleaseIngestionEngine releaseIngestionEngine, IContentProcessor contentProcessor, ServerUploadPlanner serverUploadPlanner, ReleasePackageBuilder releasePackageBuilder)
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
        _releaseIngestionEngine = releaseIngestionEngine;
        _contentProcessor = contentProcessor;
        _serverUploadPlanner = serverUploadPlanner;
        _releasePackageBuilder = releasePackageBuilder;
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

        await _progress.RunStatusAsync($"Checking existing releases for {version} ...", async _ =>
        {
            var existingReleases = await _api.GetReleasesForRepoAsync(_repository.Id);
            if (existingReleases is { Count: > 0 } &&
                existingReleases.Any(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Release with version {version} already exists.");
            }
        });

        var componentsByName = new ConcurrentDictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        var inputItems = new ConcurrentBag<InputItem>();

        var cacheHits = 0;
        var cacheMisses = 0;

        using var throttler = new SemaphoreSlim(_concurrency);

        await _progress.RunStatusAsync($"Resolving SVN files into local inputs for {tagName} ...", async ctx =>
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
                    var relativePath = NormalizeReleasePath(componentName, releaseFileName);

                    var candidateKeyHash = HashStringToHex(file.CandidateKey);
                    var aliasFileName = $"{candidateKeyHash}.bin";
                    var aliasPath = Path.Combine(aliasRoot, aliasFileName);

                    string? backingPath = null;
                    long fileSize = 0;

                    var cached = await _state.TryGetCachedFileAsync(file.CandidateKey);
                    if (cached != null)
                    {
                        var fileHash = Hash32.FromHexString(cached.FileHashHex);
                        var canonicalPath = Path.Combine(contentRoot, $"{fileHash.ToHexString()}.bin");

                        backingPath = File.Exists(aliasPath)
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

                            fileSize = cached.FileSize;
                            Interlocked.Increment(ref cacheHits);
                        }
                    }

                    if (backingPath == null)
                    {
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
                            fileSize = ingestResult.FileSize;
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

                            var cachedMap = ChunkMapCacheExtensions.ToCached(
                                fileHash.ToHexString(),
                                fileSize,
                                RebindChunkMapEntries(ingestResult.ChunkMap, canonicalPath));

                            var cachedJson = JsonSerializer.Serialize(cachedMap);

                            await _state.SaveCachedFileAsync(new CachedFileResult(
                                file.CandidateKey,
                                fileHash.ToHexString(),
                                fileSize,
                                cachedJson));

                            backingPath = aliasPath;
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
                    }

                    if (!string.IsNullOrWhiteSpace(backingPath) && File.Exists(backingPath))
                    {
                        var component = GetOrAddComponent(componentsByName, componentName);

                        inputItems.Add(new InputItem(
                            AbsolutePath: backingPath,
                            RelativePath: relativePath,
                            RelativePathWithinComponent: releaseFileName.Replace('\\', '/').TrimStart('/'),
                            Component: component,
                            Length: fileSize > 0 ? fileSize : new FileInfo(backingPath).Length,
                            LastWriteTimeUtc: /*file.LastChangedAt ??*/ DateTimeOffset.UtcNow));
                    }

                    var done = Interlocked.Increment(ref completed);
                    ctx.Status($"Resolving files {done}/{total} (cache hits: {cacheHits}, misses: {cacheMisses}) ...");
                }
                finally
                {
                    throttler.Release();
                }
            }));
        });

        _progress.Info($"{tagName}: resolved {inputItems.Count} input files, cache hits={cacheHits}, misses={cacheMisses}");

        var componentMap = componentsByName.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var inputList = inputItems.ToList();

        var ingestionResult = await _releaseIngestionEngine.IngestAsync(
            inputList,
            componentMap,
            CancellationToken.None);

        _progress.Info($"{tagName}: ingestion produced {ingestionResult.OutputArtifacts.Count} output artifacts");
        _progress.Info($"{tagName}: ingestion produced {ingestionResult.StorageWorkItems.Count} storage work items");
        _progress.Info($"{tagName}: ingestion produced {ingestionResult.LogicalArtifacts.Count} logical artifacts");

        var ingestSessionId = Guid.Empty;
        await _progress.RunStatusAsync($"Creating ingest session for {version} ...",
            async _ => { ingestSessionId = await _api.CreateIngestSessionAsync(_repository.Id, version); });

        StorageHashingResult hashingResult = null!;
        await _progress.RunStatusAsync($"Hashing storage work items for {version} ...", _ =>
        {
            try
            {
                hashingResult = _contentProcessor.HashStorageWorkItems(ingestionResult);
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        });

        // ReSharper disable once UseOfPossiblyUnassignedValue
        var bindContext = new IngestionExecutionContext(ingestionResult);
        foreach (var item in hashingResult.ItemResults)
        {
            var workItem = hashingResult.WorkItems.First(x => string.Equals(x.Identity, item.WorkItemIdentity, StringComparison.OrdinalIgnoreCase));
            bindContext.BindStoredContent(workItem, item.Hash, item.Length);
        }

        _progress.Info($"{tagName}: computed {hashingResult.ContentHashes.Count} unique stored content hashes");

        List<Hash32> missingFileChecksums = [];
        await _progress.RunStatusAsync($"Checking missing stored contents for {version} ...", async _ =>
        {
            missingFileChecksums = await _api.GetMissingFileChecksumsAsync(_repository.Id, ingestSessionId, hashingResult.ContentHashes.Keys.ToList());
        });

        _progress.Info($"{tagName}: missing stored contents = {missingFileChecksums.Count}");

        ChunkMapGenerationResult chunkMapResult = null!;
        await _progress.RunStatusAsync($"Generating chunk maps for missing stored contents for {version} ...", _ =>
        {
            try
            {
                chunkMapResult = _contentProcessor.GenerateChunkMaps(hashingResult, ingestionResult, _chunker, new HashSet<Hash32>(missingFileChecksums));
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        });

        // ReSharper disable once UseOfPossiblyUnassignedValue
        foreach (var kvp in chunkMapResult.FileChunkMaps)
            bindContext.BindChunkMap(kvp.Key, kvp.Value);

        _progress.Info($"{tagName}: generated chunk maps for {chunkMapResult.FileChunkMaps.Count} stored contents");

        var uploadPlan = await _serverUploadPlanner.CreateAsync(
            _api,
            _repository.Id,
            ingestSessionId,
            hashingResult,
            chunkMapResult,
            CancellationToken.None);

        _progress.Info($"{tagName}: missing chunks = {uploadPlan.MissingChunkChecksums.Count}");

        if (uploadPlan.MissingChunkEntries.Count > 0)
        {
            await _progress.RunStatusAsync($"Uploading {uploadPlan.MissingChunkEntries.Count} chunks for {version} ...", async ctx =>
            {
                await _ingestClient.UploadChunksAsync(
                    _repository.Id,
                    ingestSessionId,
                    _chunker,
                    uploadPlan.MissingChunkEntries,
                    progressCallback: (uploaded, total) =>
                    {
                        ctx.Status($"Uploading chunks {uploaded}/{total} ...");
                        return Task.CompletedTask;
                    });
            });
        }

        if (uploadPlan.FileDefinitions.Count > 0)
        {
            await _progress.RunStatusAsync($"Uploading {uploadPlan.FileDefinitions.Count} stored content definitions for {version} ...", async ctx =>
            {
                await _ingestClient.UploadFileDefinitionsAsync(
                    _repository.Id,
                    ingestSessionId,
                    uploadPlan.FileDefinitions.ToDictionary(x => x.Key, x => x.Value),
                    progressCallback: (uploaded, total) =>
                    {
                        ctx.Status($"Uploading file definitions {uploaded}/{total} ...");
                        return Task.CompletedTask;
                    });
            });
        }

        var customProperties = new Dictionary<string, string>
        {
            ["svn:tag"] = tagName,
            ["svn:url"] = tagUrl
        };

        if (tagLog is not null)
        {
            if (tagLog.Revision != null)
                customProperties["svn:revision"] = tagLog.Revision.ToString()!;
            if (!string.IsNullOrWhiteSpace(tagLog.Author))
                customProperties["svn:author"] = tagLog.Author;
        }

        var request = new ReleaseAddOrchestrationRequest(
            Version: version,
            Notes: string.IsNullOrEmpty(tagLog?.Message) ? $"Imported from SVN tag {tagName}" : tagLog!.Message,
            RepositoryName: _repository.Name,
            RootFolder: string.Empty,
            ComponentMapFile: null,
            CustomProperties: customProperties);

        var releasePackage = _releasePackageBuilder.Build(
            request,
            ingestionResult,
            _repository.Id);

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

    private static Component GetOrAddComponent(ConcurrentDictionary<string, Component> componentsByName, string componentName)
    {
        return componentsByName.GetOrAdd(componentName, name => new Component
        {
            Name = name,
            Files = new List<ReleaseFile>()
        });
    }

    private static string NormalizeReleasePath(string componentName, string releaseFileName)
    {
        var normalized = releaseFileName.Replace('\\', '/').TrimStart('/');

        return componentName.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{componentName}/{normalized}";
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