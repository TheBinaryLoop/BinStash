// Copyright (C) 2025-2026  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Compression;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using BinStash.Server.Helpers;
using BinStash.Server.Services.ChunkStores;
using Microsoft.EntityFrameworkCore;
using ZstdNet;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;

namespace BinStash.Server.Endpoints;

public static class ReleaseEndpoints
{
    public static RouteGroupBuilder MapReleaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId:guid}/repositories/{repoId:guid}/releases")
            .WithTags("Releases")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();
        
        group.MapGet("/{id:guid}", GetReleaseByIdAsync)
            .WithDescription("Get a release by ID.")
            .WithSummary("Get Release")
            .Produces<ReleaseSummaryDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireRepoPermission(RepositoryPermission.Read);

        group.MapGet("/{id:guid}/properties", GetReleaseCustomPropertiesAsync)
            .WithDescription("Get custom properties of a release.")
            .WithSummary("Get Release Custom Properties")
            .Produces<string>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireRepoPermission(RepositoryPermission.Read);
        
        group.MapGet("/{id:guid}/download", GetReleaseDownloadAsync)
            .WithDescription("Download a release package.")
            .WithSummary("Download Release")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireRepoPermission(RepositoryPermission.Read);
        
        /*group.MapGet("/{id:guid}/stream", GetReleaseStreamAsync)!
            .WithDescription("Stream a release package.")
            .WithSummary("Stream Release")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireRepoPermission(RepositoryPermission.Read);*/
        
        // TODO: DELETE /api/releases/{id:guid}

        return group;
    }
    
    private static async Task<IResult> GetReleaseByIdAsync(Guid id, BinStashDbContext db)
    {
        var release = await db.Releases.AsNoTracking().Include(r => r.Repository).FirstOrDefaultAsync(r => r.Id == id);
        if (release == null)
            return Results.NotFound();

        return Results.Ok(new ReleaseSummaryDto
        {
            Id = release.Id,
            Version = release.Version,
            CreatedAt = release.CreatedAt,
            Notes = release.Notes,
            Repository = new RepositorySummaryDto
            {
                Id = release.Repository.Id,
                Name = release.Repository.Name,
                Description = release.Repository.Description,
                StorageClass = release.Repository.StorageClass,
            }
        });
    }
    
    private static async Task<IResult> GetReleaseCustomPropertiesAsync(Guid id, BinStashDbContext db)
    {
        var customProperties = await db.Releases.AsNoTracking().Where(r => r.Id == id).Select(x => x.CustomProperties).FirstOrDefaultAsync();
        return Results.Text(customProperties ?? "{}", "application/json");
    }
    
    private static async Task<IResult> GetReleaseDownloadAsync(Guid id, string? component, string? file, Guid? diffReleaseId, HttpResponse response, BinStashDbContext db, IChunkStoreService chunkStoreService)
    {
        if (!string.IsNullOrEmpty(file) && string.IsNullOrEmpty(component))
            return Results.BadRequest("Component must be specified when requesting a specific file.");

        var release = await db.Releases.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (release == null)
            return Results.NotFound();

        if (diffReleaseId is not null && diffReleaseId == release.Id)
            return Results.BadRequest("Cannot create a diff release against itself.");

        var diffRelease = await db.Releases.AsNoTracking().FirstOrDefaultAsync(r => r.Id == diffReleaseId);
        if (diffReleaseId is not null && diffRelease is null)
            return Results.NotFound("Diff release not found.");

        var repo = await db.Repositories
            .Include(r => r.ChunkStore)
            .FirstOrDefaultAsync(r => r.Id == release.RepoId);

        if (repo == null)
            return Results.NotFound("Repository not found for the release.");

        var packageData = await chunkStoreService.RetrieveReleasePackageAsync(
            repo.ChunkStore,
            release.ReleaseDefinitionChecksum.ToHexString());

        if (packageData == null)
            return Results.NotFound("Release package not found in the chunk store.");

        var releasePackage = await ReleasePackageSerializer.DeserializeAsync(packageData);

        var artifacts = ResolveDownloadArtifacts(releasePackage, component, file).ToList();
        if (artifacts.Count == 0)
        {
            if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(component))
                return Results.NotFound($"File '{file}' not found in component '{component}'.");
            return Results.NotFound("No files found for the requested component.");
        }

        var unsupportedArtifacts = artifacts
            .Where(x => x.Artifact.Backing is not OpaqueBlobBacking)
            .ToList();

        if (unsupportedArtifacts.Count > 0)
        {
            return Results.BadRequest("This download endpoint currently only supports opaque-backed artifacts. " +
                                      $"Unsupported artifacts: {string.Join(", ", unsupportedArtifacts.Select(x => x.Artifact.Path))}");
        }

        var sw = Stopwatch.StartNew();

        var opaqueArtifacts = artifacts
            .Select(x => (x.ComponentName, x.RelativePath, x.Artifact, Backing: (OpaqueBlobBacking)x.Artifact.Backing))
            .ToList();

        var fileHashes = opaqueArtifacts
            .Where(x => x.Backing.ContentHash != null)
            .Select(x => x.Backing.ContentHash!.Value)
            .Distinct()
            .ToList();

        sw.Restart();

        var fileChunkMap = new ConcurrentDictionary<Hash32, List<(Hash32 Hash, int Length)>>();

        using (var throttler = new SemaphoreSlim(32))
        {
            await Task.WhenAll(fileHashes.Select(async uniqueFileHash =>
            {
                await throttler.WaitAsync();
                try
                {
                    var fileDefinition = await chunkStoreService.RetrieveFileDefinitionAsync(
                        repo.ChunkStore,
                        uniqueFileHash.ToHexString());

                    if (fileDefinition == null)
                        throw new FileNotFoundException("File definition not found in the chunk store.");

                    var chunkList = ChecksumCompressor.TransposeDecompressHashes(fileDefinition)
                        .Select(static x => (x, -1))
                        .ToList();

                    fileChunkMap[uniqueFileHash] = chunkList;
                }
                finally
                {
                    throttler.Release();
                }
            }));
        }

        var uniqueChunks = fileChunkMap.Values
            .SelectMany(x => x.Select(c => c.Hash))
            .Distinct()
            .ToList();

        var chunkInfos = await db.Chunks.AsNoTracking()
            .Where(c => c.ChunkStoreId == repo.ChunkStoreId && uniqueChunks.Contains(c.Checksum))
            .ToListAsync();

        var chunkInfoMap = chunkInfos.ToDictionary(c => c.Checksum, c => c.Length);

        foreach (var (_, chunkList) in fileChunkMap)
        {
            for (var i = 0; i < chunkList.Count; i++)
            {
                if (!chunkInfoMap.TryGetValue(chunkList[i].Hash, out var chunkLength))
                    throw new FileNotFoundException("Chunk info not found in the database.");

                chunkList[i] = (chunkList[i].Hash, chunkLength);
            }
        }

        Debug.WriteLine($"[ReleaseDownload] Built file-chunk map with {uniqueChunks.Count} unique chunks (in {sw.ElapsedMilliseconds} ms)");
        sw.Stop();

        var fileStats = await db.FileDefinitions.AsNoTracking()
            .Where(x => x.ChunkStoreId == repo.ChunkStoreId && fileHashes.Contains(x.Checksum))
            .ToDictionaryAsync(x => x.Checksum, x => x.Length);

        response.Headers.ContentEncoding = "identity";
        response.ContentType = "application/zstd";

        var fileName = $"{(component != null ? $"component-{component}-" : "release-")}{release.Id}{(diffRelease != null ? ".diff" : "")}.tar.zst";
        response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition
        {
            FileName = fileName,
            Inline = false
        }.ToString();

        await using var compressor = new CompressionStream(response.Body);
        await using var tarWriter = new TarWriter(compressor);

        if (diffRelease is not null)
        {
            var diffPackageData = await chunkStoreService.RetrieveReleasePackageAsync(
                repo.ChunkStore,
                diffRelease.ReleaseDefinitionChecksum.ToHexString());

            if (diffPackageData == null)
                return Results.NotFound("Diff release package not found in the chunk store.");

            var diffReleasePackage = await ReleasePackageSerializer.DeserializeAsync(diffPackageData);

            var diffArtifacts = ResolveDownloadArtifacts(diffReleasePackage, component, file).ToList();
            if (diffArtifacts.Any(x => x.Artifact.Backing is not OpaqueBlobBacking))
            {
                return Results.BadRequest("Diff download currently only supports opaque-backed artifacts.");
            }

            var diffOpaqueArtifacts = diffArtifacts
                .Select(x => (x.ComponentName, x.RelativePath, x.Artifact, Backing: (OpaqueBlobBacking)x.Artifact.Backing))
                .ToList();

            var diffFileHashes = diffOpaqueArtifacts
                .Where(x => x.Backing.ContentHash != null)
                .Select(x => x.Backing.ContentHash!.Value)
                .Distinct()
                .Except(fileHashes)
                .ToList();

            var diffFileChunkMap = new ConcurrentDictionary<Hash32, List<(Hash32 Hash, int Length)>>();

            using (var throttler = new SemaphoreSlim(32))
            {
                await Task.WhenAll(diffFileHashes.Select(async uniqueFileHash =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        var fileDefinition = await chunkStoreService.RetrieveFileDefinitionAsync(
                            repo.ChunkStore,
                            uniqueFileHash.ToHexString());

                        if (fileDefinition == null)
                            throw new FileNotFoundException("File definition not found in the chunk store.");

                        var chunkList = ChecksumCompressor.TransposeDecompressHashes(fileDefinition)
                            .Select(static x => (x, -1))
                            .ToList();

                        diffFileChunkMap[uniqueFileHash] = chunkList;
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }));
            }

            var diffUniqueChunks = diffFileChunkMap.Values
                .SelectMany(x => x.Select(c => c.Hash))
                .Distinct()
                .Except(uniqueChunks)
                .ToList();

            var diffChunkInfos = await db.Chunks.AsNoTracking()
                .Where(c => c.ChunkStoreId == repo.ChunkStoreId && diffUniqueChunks.Contains(c.Checksum))
                .ToListAsync();

            chunkInfos.AddRange(diffChunkInfos);

            foreach (var chunk in diffChunkInfos)
                chunkInfoMap[chunk.Checksum] = chunk.Length;

            foreach (var (_, chunkList) in diffFileChunkMap)
            {
                for (var i = 0; i < chunkList.Count; i++)
                {
                    if (!chunkInfoMap.TryGetValue(chunkList[i].Hash, out var chunkLength))
                        throw new FileNotFoundException("Chunk info not found in the database.");

                    chunkList[i] = (chunkList[i].Hash, chunkLength);
                }
            }

            foreach (var kvp in diffFileChunkMap)
                fileChunkMap.TryAdd(kvp.Key, kvp.Value);

            var currentFiles = ToLegacyDeltaFiles(opaqueArtifacts);
            var baseFiles = ToLegacyDeltaFiles(diffOpaqueArtifacts);

            var (deltaManifest, newChunkChecksums, newFileChecksums) = DeltaCalculator.ComputeDeltaManifest(
                baseFiles,
                currentFiles,
                fileChunkMap.ToDictionary(),
                chunkInfos);

            deltaManifest = deltaManifest with
            {
                BaseReleaseId = diffRelease.Id.ToString(),
                TargetReleaseId = release.Id.ToString(),
            };

            var deltaManifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(deltaManifest));
            await tarWriter.WriteFileAsync("delta-manifest.json", deltaManifestBytes);

            foreach (var newChunks in newChunkChecksums)
            {
                var chunkData = await chunkStoreService.RetrieveChunkAsync(repo.ChunkStore, newChunks.ToHexString());
                if (chunkData == null)
                    return Results.NotFound($"Chunk {newChunks} not found in the chunk store.");

                await tarWriter.WriteFileAsync($"chunks/{newChunks.ToHexString()}", chunkData);
            }

            foreach (var newFileChecksum in newFileChecksums)
            {
                var fileDefinition = fileChunkMap[newFileChecksum];
                await tarWriter.WriteFileAsync($"files/{newFileChecksum.ToHexString()}", async outputStream =>
                {
                    for (var i = 0; i < fileDefinition.Count; i++)
                    {
                        var chunkData = await chunkStoreService.RetrieveChunkAsync(repo.ChunkStore, fileDefinition[i].Hash.ToHexString());
                        
                        if (chunkData == null)
                            throw new FileNotFoundException($"Chunk {fileDefinition[i].Hash} not found in the chunk store.");

                        await outputStream.WriteAsync(chunkData, 0, fileDefinition[i].Length);
                    }
                }, fileDefinition.Sum(x => x.Length));
            }
        }
        else
        {
            foreach (var artifactEntry in opaqueArtifacts)
            {
                var contentHash = artifactEntry.Backing.ContentHash
                                  ?? throw new InvalidOperationException($"Opaque artifact '{artifactEntry.Artifact.Path}' has no content hash.");

                var relativePath = artifactEntry.RelativePath.Replace('\\', '/');
                if (string.IsNullOrEmpty(component))
                    relativePath = $"{artifactEntry.ComponentName}/{relativePath}";
                var totalSize = artifactEntry.Backing.Length ?? fileStats[contentHash];

                await tarWriter.WriteFileAsync(relativePath, async outputStream =>
                {
                    var currentFileChunkMap = fileChunkMap[contentHash];

                    var chunksByIndex = new Dictionary<int, Hash32>(currentFileChunkMap.Count);
                    for (var i = 0; i < currentFileChunkMap.Count; i++)
                        chunksByIndex[i] = currentFileChunkMap[i].Hash;

                    var chunkRefs = BuildSequentialChunkRefs(currentFileChunkMap);

                    await WriteChunksWindowedAsync(
                        outputStream,
                        chunkRefs,
                        chunksByIndex,
                        chunkStoreService,
                        repo.ChunkStore,
                        windowSize: 32);
                }, totalSize, release.CreatedAt.UtcDateTime);

                await tarWriter.WriteFileAsync($"{relativePath}.hash", contentHash.GetBytes());
            }
        }

        return Results.Empty;
    }

    /*private static async Task<IResult> GetReleaseStreamAsync(Guid id, string? component, string? file, Guid? diffReleaseId, HttpResponse response, BinStashDbContext db, CancellationToken ct)
    {
        try
        {
            response.ContentType = "application/vnd.binstash.release-stream";
            response.Headers.ContentEncoding = "identity";
            response.StatusCode = StatusCodes.Status200OK;
        
            var plan = await BuildApplyPlanAsync(id, diffReleaseId, component, file, db, ct);
        
            var fileName =
                $"{(component != null ? $"component-{component}-" : "release-")}{id}" +
                $"{(diffReleaseId != null ? ".diff" : "")}.brs";

            response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition
            {
                FileName = fileName,
                Inline = false
            }.ToString();
            
            await using var writer = new ReleaseStreamWriter(response.Body);

            await writer.WritePreambleAsync(ct);

            await writer.WriteReleaseHeaderAsync(new ReleaseHeaderPayload
            {
                ReleaseId = plan.ReleaseId.ToString(),
                BasisReleaseId = plan.BasisReleaseId?.ToString() ?? string.Empty,
                FileCount = (ulong)plan.Files.Count,
                Flags = plan.BasisReleaseId is null ? 0UL : 1UL
            }, ct);

            foreach (var planFile in plan.Files)
            {
                ct.ThrowIfCancellationRequested();

                await writer.WriteFileStartAsync(new FileStartPayload
                {
                    FileId = 0, // not used yet; path is enough for now
                    RelativePath = planFile.RelativePath,
                    FinalLength = (ulong)planFile.Length,
                    FinalHash = planFile.FileHash.GetBytes(),
                    UnixMode = 0
                }, ct);

                foreach (var op in planFile.Operations.OrderBy(x => x.OutputOffset))
                {
                    switch (op)
                    {
                        case InlineWriteOp inline:
                            await writer.WriteInlineDataAsync(new InlineDataPayload
                            {
                                FileId = 0,
                                OutputOffset = (ulong)inline.OutputOffset,
                                Encoding = 0, // raw
                                LogicalLength = (ulong)inline.Length,
                                Data = inline.Bytes
                            }, ct);
                            break;

                        case CopyFromBasisOp copy:
                            await writer.WriteCopyFromBasisAsync(new CopyFromBasisPayload
                            {
                                FileId = 0,
                                OutputOffset = (ulong)copy.OutputOffset,
                                BasisPath = copy.BasisRelativePath,
                                BasisOffset = (ulong)copy.BasisOffset,
                                Length = (ulong)copy.Length
                            }, ct);
                            break;

                        default:
                            throw new NotSupportedException($"Unsupported apply op type: {op.GetType().Name}");
                    }
                }

                await writer.WriteFileEndAsync(new FileEndPayload
                {
                    FileId = 0
                }, ct);

                await response.Body.FlushAsync(ct);
            }

            await writer.WriteEndOfStreamAsync(ct);
            await response.Body.FlushAsync(ct);

            return Results.Empty;
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(ex.Message);
        }
    }*/
    
    private static async Task WriteChunksWindowedAsync(Stream outputStream, IReadOnlyList<ChunkRef> chunkRefs, IReadOnlyDictionary<int, Hash32> chunksByIndex, IChunkStoreService chunkStoreService, ChunkStore store, int windowSize = 32, CancellationToken cancellationToken = default)
    {
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be greater than 0.");

        // Maps absolute chunk position in chunkRefs -> in-flight read task
        var inFlight = new Dictionary<int, Task<byte[]>>(windowSize);

        Task<byte[]> StartReadAsync(int position)
        {
            var chunkRef = chunkRefs[position];
            if (!chunksByIndex.TryGetValue(chunkRef.Index, out var checksum))
                throw new KeyNotFoundException($"No checksum found for chunk index {chunkRef.Index}.");

            return ReadChunkAsync(checksum, chunkStoreService, store, cancellationToken);
        }

        // Prime the pipeline
        var initialCount = Math.Min(windowSize, chunkRefs.Count);
        for (var i = 0; i < initialCount; i++)
            inFlight[i] = StartReadAsync(i);

        // Consume in order, topping up the window as we go
        for (var nextToWrite = 0; nextToWrite < chunkRefs.Count; nextToWrite++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkData = await inFlight[nextToWrite].ConfigureAwait(false);
            inFlight.Remove(nextToWrite);

            await outputStream.WriteAsync(chunkData, cancellationToken).ConfigureAwait(false);

            var nextToSchedule = nextToWrite + windowSize;
            if (nextToSchedule < chunkRefs.Count)
                inFlight[nextToSchedule] = StartReadAsync(nextToSchedule);
        }
    }

    private static async Task<byte[]> ReadChunkAsync(Hash32 checksum, IChunkStoreService chunkStoreService, ChunkStore store, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chunkData = await chunkStoreService.RetrieveChunkAsync(store, checksum.ToHexString()).ConfigureAwait(false);
        if (chunkData == null)
            throw new FileNotFoundException($"Chunk {checksum.ToHexString()} not found in the chunk store.");

        return chunkData;
    }
    
    private static List<(string ComponentName, string RelativePath, OutputArtifact Artifact)> ResolveDownloadArtifacts(ReleasePackage releasePackage, string? component, string? file)
    {
        var artifacts = releasePackage.OutputArtifacts
            .Where(x => x.Kind == OutputArtifactKind.File)
            .Select(x => (
                x.ComponentName,
                RelativePath: GetRelativePathWithinComponent(x),
                Artifact: x))
            .ToList();

        if (!string.IsNullOrEmpty(component))
        {
            artifacts = artifacts
                .Where(x => x.ComponentName.Equals(component, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(component))
        {
            artifacts = artifacts
                .Where(x =>
                    x.ComponentName.Equals(component, StringComparison.OrdinalIgnoreCase) &&
                    x.RelativePath.Equals(file, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return artifacts;
    }

    private static string GetRelativePathWithinComponent(OutputArtifact artifact)
    {
        var normalizedPath = artifact.Path.Replace('\\', '/').TrimStart('/');
        var normalizedComponent = artifact.ComponentName.Replace('\\', '/').Trim('/');

        if (string.IsNullOrWhiteSpace(normalizedComponent) ||
            normalizedComponent.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath;
        }

        var prefix = normalizedComponent + "/";
        if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return normalizedPath[prefix.Length..];

        return normalizedPath;
    }

    private static List<(string Component, ReleaseFile File)> ToLegacyDeltaFiles(
        IEnumerable<(string ComponentName, string RelativePath, OutputArtifact Artifact, OpaqueBlobBacking Backing)> artifacts)
    {
        return artifacts
            .Select(x => (
                Component: x.ComponentName,
                File: new ReleaseFile
                {
                    Name = x.RelativePath.Replace('\\', '/'),
                    Hash = x.Backing.ContentHash ?? default,
                    Chunks = new List<DeltaChunkRef>()
                }))
            .ToList();
    }

    private static List<ChunkRef> BuildSequentialChunkRefs(List<(Hash32 Hash, int Length)> chunkMap)
    {
        var result = new List<ChunkRef>(chunkMap.Count);
        long offset = 0;

        for (var i = 0; i < chunkMap.Count; i++)
        {
            var (_, length) = chunkMap[i];
            result.Add(new ChunkRef
            {
                Index = i,
                Offset = offset,
                Length = length
            });
            offset += length;
        }

        return result;
    }
    
}
