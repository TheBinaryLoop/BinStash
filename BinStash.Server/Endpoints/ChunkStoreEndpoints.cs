// Copyright (C) 2025  Lukas Eßmann
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
using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Core.Serialization.Utils;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using ZstdNet;

namespace BinStash.Server.Endpoints;

public static class ChunkStoreEndpoints
{
    public static RouteGroupBuilder MapChunkStoreEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Add ProducesError
        
        var group = app.MapGroup("/api/chunkstores")
            .WithTags("ChunkStore")
            .RequireAuthorization();
            //.WithDescription("Endpoints for managing chunk stores. Chunk stores are used to store chunks of data that are referenced by repositories. They can be local or remote, and support various chunking algorithms.");

        group.MapPost("/", CreateChunkStoreAsync)
            .WithDescription("Creates a new chunk store.")
            .WithSummary("Create Chunk Store")
            .Produces<ChunkStoreSummaryDto>(StatusCodes.Status201Created);
        group.MapGet("/", ListChunkStoresAsync)
            .WithDescription("Lists all chunk stores.")
            .WithSummary("List Chunk Stores")
            .Produces<List<ChunkStoreSummaryDto>>();
        group.MapGet("/{id:guid}", GetChunkStoreByIdAsync)
            .WithDescription("Gets a chunk store by its ID.")
            .WithSummary("Get Chunk Store By ID")
            .Produces<ChunkStoreDetailDto>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/rebuild", RebuildChunkStoreAsync)
            .WithDescription(
                "Rebuilds the chunk store by scanning the underlying storage and rewriting the pack and index files.")
            .WithSummary("Rebuild Chunk Store")
            .Produces(StatusCodes.Status200OK);
        group.MapGet("/{id:guid}/upgrade", UpgradeReleasesToLatestVersionAsync)
            .WithDescription("Upgrades all releases in the chunk store to the latest version.")
            .WithSummary("Upgrade Releases")
            .Produces(StatusCodes.Status200OK);
        group.MapPost("/{id:guid}/files/missing", GetMissingFileDefinitionsAsync)
            .WithDescription("Gets a list of missing file definitions in the chunk store.")
            .WithSummary("Get Missing File Definitions")
            .Produces<byte[]>(contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        group.MapPost("/{id:guid}/files/batch", UploadFileDefinitionsBatchAsync)
            .WithDescription("Uploads a batch of file definitions to the chunk store.")
            .WithSummary("Upload File Definitions Batch")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        /*group.MapDelete("/{id:guid}", DeleteChunkStoreAsync)
            .WithDescription("Deletes a chunk store by its ID.")
            .WithSummary("Delete Chunk Store")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);*/
        group.MapPost("/{id:guid}/chunks/missing", GetMissingChunksAsync)
            .WithDescription("Gets a list of missing chunks in the chunk store.")
            .WithSummary("Get Missing Chunks")
            .Produces<byte[]>(contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        group.MapPost("/{id:guid}/chunks/{chunkChecksum:length(64)}", UploadChunkAsync)
            .WithDescription("Uploads a single chunk to the chunk store.")
            .WithSummary("Upload Chunk")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        group.MapPost("/{id:guid}/chunks/batch", UploadChunksBatchAsync)
            .WithDescription("Uploads a batch of chunks to the chunk store.")
            .WithSummary("Upload Chunks Batch")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> CreateChunkStoreAsync(CreateChunkStoreDto dto, BinStashDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest("Chunk store name is required.");
        
        if (db.ChunkStores.Any(x => x.Name == dto.Name))
            return Results.Conflict($"A chunk store with the name '{dto.Name}' already exists.");

        // Check if the type is valid, otherwise return non-success
        var isValidChunkStoreType = Enum.TryParse<ChunkStoreType>(dto.Type, true, out var chunkStoreType);
        if (!isValidChunkStoreType)
            return Results.BadRequest($"Invalid chunk store type '{dto.Type}'.");
        
        // Check if the local path is valid for local chunk store type
        if (chunkStoreType == ChunkStoreType.Local)
        {
            if (string.IsNullOrWhiteSpace(dto.LocalPath))
                return Results.BadRequest("Local path is required for local chunk store type.");
        }
        
        // Validate chunker options or set defaults
        var chunkerOptions = dto.Chunker == null ? ChunkerOptions.Default(ChunkerType.FastCdc) : new ChunkerOptions
        {
            Type = Enum.TryParse<ChunkerType>(dto.Chunker.Type, true, out var chunkerType) ? chunkerType : ChunkerType.FastCdc,
            MinChunkSize = dto.Chunker.MinChunkSize ?? 2048,
            AvgChunkSize = dto.Chunker.AvgChunkSize ?? 8192,
            MaxChunkSize = dto.Chunker.MaxChunkSize ?? 65536
        };
        
        if (chunkerOptions.MinChunkSize <= 0 || chunkerOptions.AvgChunkSize <= 0 || chunkerOptions.MaxChunkSize <= 0)
            return Results.BadRequest("Chunk sizes must be greater than zero.");
        
        var chunkStore = new ChunkStore(dto.Name, chunkStoreType, dto.LocalPath, new LocalFolderObjectStorage(dto.LocalPath))
        {
            ChunkerOptions = chunkerOptions
        };

        db.ChunkStores.Add(chunkStore);
        await db.SaveChangesAsync();

        return Results.Created($"/api/chunkstores/{chunkStore.Id}", new ChunkStoreSummaryDto
        {
            Id = chunkStore.Id,
            Name = chunkStore.Name
        });
    }
    
    private static async Task<IResult> ListChunkStoresAsync(BinStashDbContext db)
    {
        var stores = await db.ChunkStores.Select(x => new ChunkStoreSummaryDto
        {
            Id = x.Id,
            Name = x.Name
        }).ToListAsync();
        return Results.Ok(stores);
    }
    
    private static async Task<IResult> GetChunkStoreByIdAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();

        return Results.Ok(new ChunkStoreDetailDto
        {
            Id = store.Id,
            Name = store.Name,
            Type = store.Type.ToString(),
            Chunker = new ChunkStoreChunkerDto
            {
                Type = store.ChunkerOptions.Type.ToString(),
                MinChunkSize = store.ChunkerOptions.MinChunkSize,
                AvgChunkSize = store.ChunkerOptions.AvgChunkSize,
                MaxChunkSize = store.ChunkerOptions.MaxChunkSize,
            },
            Stats = new Dictionary<string, object>() // await new LocalFolderChunkStorage(store.LocalPath).GetStorageStatsAsync()
        });
    }

    private static async Task<IResult> RebuildChunkStoreAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
        
        store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));

        var result = await store.RebuildStorageAsync();
        if (!result)
            return Results.Problem("Failed to rebuild chunk store.");
        
        return Results.Ok();
        
    }
    
    private static async Task<IResult> UpgradeReleasesToLatestVersionAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
        
        var repos = await db.Repositories.Where(x => x.ChunkStoreId == id).ToListAsync();
        if (!repos.Any())
            return Results.Ok();
        
        var repoIds = repos.Select(x => x.Id).ToList();
        
        var releasesToUpgrade = await db.Releases.Where(r => repoIds.Contains(r.RepoId) && r.SerializerVersion < ReleasePackageSerializer.Version).ToListAsync();
        if (!releasesToUpgrade.Any())
            return Results.Ok();
        
        store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
        
        foreach (var releaseFile in Directory.EnumerateFiles(Path.Combine(store.LocalPath, "Releases"), "*.rdef",
                     SearchOption.AllDirectories))
        {
            var tmpHash = Hash32.FromHexString(Path.GetFileNameWithoutExtension(releaseFile));
            var tmpData = await File.ReadAllBytesAsync(releaseFile);
            var tmpReleasePack = await ReleasePackageSerializer.DeserializeAsync(tmpData);
            var tmpReleaseId = Guid.Parse(tmpReleasePack.ReleaseId);
            var dbRelease = releasesToUpgrade.FirstOrDefault(r => r.Id == tmpReleaseId);
            if (dbRelease == null)
            {
                File.Delete(releaseFile);
                continue;
            }
            if (dbRelease.ReleaseDefinitionChecksum == tmpHash) continue;
            dbRelease.ReleaseDefinitionChecksum = tmpHash;
        }

        var failedReleases = new Dictionary<Release, string>();
        var grownReleases = new Dictionary<Release, int>(); // Release, grown size in bytes
        
        foreach (var release in releasesToUpgrade)
        {
            var releaseData = await store.RetrieveReleasePackageAsync(release.ReleaseDefinitionChecksum.ToHexString());
            if (releaseData == null)
            {
                failedReleases[release] = "Failed to retrieve release package.";
                continue;
            }

            ReleasePackage releasePackage;
            try
            {
                releasePackage = await ReleasePackageSerializer.DeserializeAsync(releaseData);
            }
            catch (Exception e)
            {
                failedReleases[release] = $"Failed to deserialize release package: {e.Message}";
                continue;
            }
            
            var oldReleaseDefinitionChecksum = release.ReleaseDefinitionChecksum;

            var serializedReleasePackage = await ReleasePackageSerializer.SerializeAsync(releasePackage);
            
            if (serializedReleasePackage.Length > releaseData.Length)
                grownReleases[release] = serializedReleasePackage.Length - releaseData.Length;
            
            var hash = new Hash32(Blake3.Hasher.Hash(serializedReleasePackage).AsSpan());
            await store.StoreReleasePackageAsync(serializedReleasePackage);
            release.SerializerVersion = ReleasePackageSerializer.Version;
            release.ReleaseDefinitionChecksum = hash;
            await db.SaveChangesAsync();
            try
            {
               await store.DeleteReleasePackageAsync(oldReleaseDefinitionChecksum.ToHexString());
            }
            catch (Exception e)
            {
                failedReleases[release] = $"Failed to delete old release package: {e.Message}";
            }
        }
        return Results.Json(new { failedReleases = failedReleases.Select(x => new {x.Key.Id, x.Value}).ToList(), grownReleases = grownReleases.Select(x => new {x.Key.Id, x.Value}).ToList(), totalSizeGrowth = grownReleases.Sum(x => x.Value), totalReleases = releasesToUpgrade.Count, successfulUpgrades = releasesToUpgrade.Count - failedReleases.Count });
    }

    private static async Task<IResult> DeleteChunkStoreAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
            
        // TODO: Check if the store is in use by any repository before deleting
        // TODO: Delete the physical store if it's a local store

        return Results.Conflict();
    }

    private static async Task<IResult> GetMissingFileDefinitionsAsync(Guid id, HttpRequest request, BinStashDbContext db)
    {
        try
        {
            var fileDefinitionChecksums = (await ChecksumCompressor.TransposeDecompressAsync(request.Body)).Select(x => new Hash32(x)).ToArray();
            
            var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();
            
            // Return a list of all file definitions that are in the request but not in the database with the store id
            if (!fileDefinitionChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");
            
            var knownChecksums = db.FileDefinitions
                .Where(c => c.ChunkStoreId == id && fileDefinitionChecksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToList();
            
            var missingChecksums = fileDefinitionChecksums.Except(knownChecksums).ToList();
            if (!missingChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");
            
            return Results.Bytes(ChecksumCompressor.TransposeCompress(missingChecksums.Select(x => x.GetBytes()).ToList()), "application/octet-stream");
        }
        catch (Exception) 
        {
            return Results.BadRequest("Invalid request body.");
        }
    }
    
    private static async Task<IResult> UploadFileDefinitionsBatchAsync(Guid id, BinStashDbContext db, HttpRequest request)
    {
        // Check for the ingest id header X-Ingest-Session-Id
        if (!request.Headers.TryGetValue("X-Ingest-Session-Id", out var ingestIdHeaders) || !Guid.TryParse(ingestIdHeaders.First(), out var ingestId))
            return Results.BadRequest("Missing or invalid X-Ingest-Session-Id header.");
        
        var ingestSession = await db.IngestSessions.FindAsync(ingestId);
        if (ingestSession == null)
            return Results.BadRequest("Invalid X-Ingest-Session-Id header value.");
        
        if (ingestSession.State == IngestSessionState.Completed || ingestSession.State == IngestSessionState.Failed || ingestSession.State == IngestSessionState.Aborted || ingestSession.State == IngestSessionState.Expired || ingestSession.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.BadRequest("Ingest session is not active.");
        
        if (ingestSession.State == IngestSessionState.Created)
            ingestSession.State = IngestSessionState.InProgress;

        var fileDefinitions = new Dictionary<Hash32, (List<Hash32> Chunks, long Length)>();
        /*using var ms = new MemoryStream();
        await using var decompressionStream = new ZstdNet.DecompressionStream(request.Body);
        await decompressionStream.CopyToAsync(ms);
        ms.Position = 0;*/
        await using var decompressionStream = new DecompressionStream(request.Body);
        using var reader = new BinaryReader(decompressionStream);
        var chunkChecksums = await ChecksumCompressor.TransposeDecompressAsync(decompressionStream);
        var batchCount = await VarIntUtils.ReadVarIntAsync<int>(decompressionStream);
        for (var i = 0; i < batchCount; i++)
        {
            var fileChecksum = new Hash32(reader.ReadBytes(32));
            var fileLength = await VarIntUtils.ReadVarIntAsync<long>(decompressionStream);
            var chunkCount = await VarIntUtils.ReadVarIntAsync<int>(decompressionStream);
            var chunks = new List<Hash32>(chunkCount);
            for (var j = 0; j < chunkCount; j++)
            {
                var chunkIndex = await VarIntUtils.ReadVarIntAsync<int>(decompressionStream);
                if (chunkIndex < 0 || chunkIndex >= chunkChecksums.Count)
                    return Results.BadRequest("Invalid chunk index in batch.");
                chunks.Add(new Hash32(chunkChecksums[chunkIndex]));
            }
            fileDefinitions[fileChecksum] = (chunks, fileLength);
        }
        
        ingestSession.FilesSeenTotal += fileDefinitions.Count;
        
        var storeMeta = await db.ChunkStores.FindAsync(id);
        if (storeMeta == null)
            return Results.NotFound();

        var fileHashes = fileDefinitions.Keys.ToList();
        var existingFiles = db.FileDefinitions.Where(x => x.ChunkStoreId == id && fileHashes.Contains(x.Checksum)).Select(x => x.Checksum).ToList();
        
        var store = new ChunkStore(storeMeta.Name, storeMeta.Type, storeMeta.LocalPath, new LocalFolderObjectStorage(storeMeta.LocalPath));

        foreach (var fileDefinition in fileDefinitions.Where(x => !existingFiles.Contains(x.Key)))
        {
            var entry = new FileDefinition
            {
                Checksum = fileDefinition.Key,
                ChunkStoreId = id,
                Length = fileDefinition.Value.Length,
            };
            
            var storeFileDefinitionResult = await store.StoreFileDefinitionAsync(fileDefinition.Key, ChecksumCompressor.TransposeCompress(fileDefinition.Value.Chunks.Select(x => x.GetBytes()).ToList()));
            
            if (!storeFileDefinitionResult.Success)
                return Results.Problem($"Failed to store file definition ({fileDefinition.Key.ToHexString()}) in chunk store.");
            
            ingestSession.FilesSeenUnique++;
            ingestSession.FilesSeenNew++;
            ingestSession.MetadataSize += storeFileDefinitionResult.BytesWritten;
            
            db.FileDefinitions.Add(entry);
        }
        
        ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        
        await db.SaveChangesAsync();
        
        return Results.Created();
    }
    
    private static async Task<IResult> GetMissingChunksAsync(Guid id, HttpRequest request, BinStashDbContext db)
    {
        try
        {
            var chunkChecksums = (await ChecksumCompressor.TransposeDecompressAsync(request.Body)).Select(x => new Hash32(x)).ToArray();
            
            var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();
            
            // Return a list of all chunks that are in the dto but not in the database with the store id
            if (!chunkChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");
            
            var knownChecksums = db.Chunks
                .Where(c => c.ChunkStoreId == id && chunkChecksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToList();
            
            var missingChecksums = chunkChecksums.Except(knownChecksums).ToList();
            
            if (!missingChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");
        
            return Results.Bytes(ChecksumCompressor.TransposeCompress(missingChecksums.Select(x => x.GetBytes()).ToList()), "application/octet-stream");
        }
        catch (Exception)
        {
            return Results.BadRequest("Invalid request body.");
        }
    }

    private static async Task<IResult> UploadChunkAsync(Guid id, string chunkChecksum, BinStashDbContext db, Stream chunkStream)
    {
        var checksum = Hash32.FromHexString(chunkChecksum);
        
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
            
        store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
            
        using var ms = new MemoryStream();
        await chunkStream.CopyToAsync(ms);
        ms.Position = 0;

        if (db.Chunks.Any(c => c.ChunkStoreId == id && c.Checksum == checksum)) return Results.Ok();
        var (success, bytesWritten) = await store.StoreChunkAsync(chunkChecksum, ms.ToArray());
        if (!success) return Results.Problem();
        db.Chunks.Add(new Chunk
        {
            Checksum = checksum,
            ChunkStoreId = id,
            Length = Convert.ToInt32(ms.Length),
            CompressedLength = bytesWritten
        });
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static async Task<IResult> UploadChunksBatchAsync(Guid id, List<ChunkUploadDto> chunks, BinStashDbContext db, HttpRequest request)
    {
        // Check for the ingest id header X-Ingest-Session-Id
        if (!request.Headers.TryGetValue("X-Ingest-Session-Id", out var ingestIdHeaders) || !Guid.TryParse(ingestIdHeaders.First(), out var ingestId))
            return Results.BadRequest("Missing or invalid X-Ingest-Session-Id header.");
        
        var ingestSession = await db.IngestSessions.FindAsync(ingestId);
        if (ingestSession == null)
            return Results.BadRequest("Invalid X-Ingest-Session-Id header value.");
        
        if (ingestSession.State == IngestSessionState.Completed || ingestSession.State == IngestSessionState.Failed || ingestSession.State == IngestSessionState.Aborted || ingestSession.State == IngestSessionState.Expired || ingestSession.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.BadRequest("Ingest session is not active.");
        
        if (ingestSession.State == IngestSessionState.Created)
            ingestSession.State = IngestSessionState.InProgress;
        
        var storeMeta = await db.ChunkStores.FindAsync(id);
        if (storeMeta == null)
            return Results.NotFound();

        var store = new ChunkStore(storeMeta.Name, storeMeta.Type, storeMeta.LocalPath, new LocalFolderObjectStorage(storeMeta.LocalPath));

        if (chunks.Count == 0)
            return Results.BadRequest("No chunks provided.");

        ingestSession.ChunksSeenTotal += chunks.Count;
        
        // Deduplicate
        var uniqueChunks = chunks
            .GroupBy(c => c.Checksum)
            .Select(g => g.First())
            .ToList();
        
        ingestSession.ChunksSeenUnique += uniqueChunks.Count;

        var checksums = uniqueChunks.Select(c => Hash32.FromHexString(c.Checksum)).ToArray();
        
        var knownChecksums = await db.Chunks
            .Where(c => c.ChunkStoreId == id && checksums.Contains(c.Checksum))
            .Select(c => c.Checksum)
            .ToListAsync();
        
        var missingChunks = uniqueChunks
            .Where(c => !knownChecksums.Contains(Hash32.FromHexString(c.Checksum)))
            .ToList();
        
        ingestSession.ChunksSeenNew += missingChunks.Count;

        var missingChunksWrittenBytes = new ConcurrentDictionary<Hash32, int>();
        
        var writeTasks = missingChunks.Select(async chunk =>
        {
            var hash = Convert.ToHexString(Blake3.Hasher.Hash(chunk.Data).AsSpan());
            if (!hash.Equals(chunk.Checksum, StringComparison.OrdinalIgnoreCase))
                return false; // Consider logging this

            var (success, bytesWritten) = await store.StoreChunkAsync(chunk.Checksum, chunk.Data);
            missingChunksWrittenBytes[Hash32.FromHexString(chunk.Checksum)] = bytesWritten;
            return success;
        });
        
        ingestSession.DataSizeTotal += missingChunksWrittenBytes.Sum(c => c.Value);
        ingestSession.DataSizeUnique += missingChunksWrittenBytes.Sum(c => c.Value);

        var results = await Task.WhenAll(writeTasks);

        if (results.Any(r => r == false))
            return Results.Problem("Some chunks failed checksum or storage.");

        
        var chunksToAdd = missingChunks.Select(chunk =>
        {
            var checksum = Hash32.FromHexString(chunk.Checksum);
            return new Chunk
            {
                Checksum = checksum,
                ChunkStoreId = id,
                Length = chunk.Data.Length,
                CompressedLength = missingChunksWrittenBytes[checksum]
            };
        });
        
        ingestSession.LastUpdatedAt = DateTimeOffset.UtcNow;
        ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        db.Chunks.AddRange(chunksToAdd);
        await db.SaveChangesAsync();

        return Results.Ok();
    }

}