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
using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Ingest;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Compression;
using BinStash.Core.Entities;
using BinStash.Core.Extensions;
using BinStash.Core.Serialization;
using BinStash.Core.Serialization.Utils;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using BinStash.Server.Context;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;
using ZstdNet;

namespace BinStash.Server.Endpoints;

public static class IngestSessionEndpoints
{
    public static RouteGroupBuilder MapIngestSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId:guid}/repositories/{repoId:guid}/ingest")
            .WithTags("Ingest Sessions")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();
        
        group.MapPost("/sessions", CreateIngestSessionAsync)
            .WithDescription("Creates a new ingest session and returns the session ID and expiry time.")
            .WithSummary("Create Ingest Session")
            .Produces<CreateIngestSessionResponse>(201)
            .Produces(400)
            .Produces(404)
            .RequireRepoPermission(RepositoryPermission.Write);
        
        // Nested session group
        var session = group.MapGroup("/sessions/{sessionId:guid}")
            .RequireRepoPermission(RepositoryPermission.Write)
            .RequireValidIngestSession();
        
        session.MapGet("/", GetSessionStatsAsync)
            .WithDescription("Gets statistics about the ingest session.")
            .WithSummary("Get Ingest Session Stats")
            .Produces<IngestSessionStatsDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        session.MapPost("/files/missing", GetMissingFileDefinitionsAsync)
            .WithDescription("Gets a list of missing file definitions in the chunk store.")
            .WithSummary("Get Missing File Definitions")
            .Produces<byte[]>(contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        session.MapPost("/files/batch", UploadFileDefinitionsBatchAsync)
            .WithDescription("Uploads a batch of file definitions to the chunk store.")
            .WithSummary("Upload File Definitions Batch")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        session.MapPost("/chunks/missing", GetMissingChunksAsync)
            .WithDescription("Gets a list of missing chunks in the chunk store.")
            .WithSummary("Get Missing Chunks")
            .Produces<byte[]>(contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        session.MapPost("/chunks/{chunkChecksum:length(64)}", UploadChunkAsync)
            .WithDescription("Uploads a single chunk to the chunk store.")
            .WithSummary("Upload Chunk")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        session.MapPost("/chunks/batch", UploadChunksBatchAsync)
            .WithDescription("Uploads a batch of chunks to the chunk store.")
            .WithSummary("Upload Chunks Batch")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        session.MapPost("/finalize", FinalizeIngestSessionAsync)
            .WithDescription("Finalizes the ingest session.")
            .WithSummary("Finalize Ingest Session")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .RequireRepoPermission(RepositoryPermission.Write);
        
        /*group.MapPost("/start", IngestSessionHandlers.StartIngestSession);
        group.MapPost("/{sessionId}/abort", IngestSessionHandlers.AbortIngestSession);*/
        return group;
    }

    private static async Task<IResult> CreateIngestSessionAsync(Guid repoId, BinStashDbContext db)
    {
        // If we have authentication, we can link the session to a user. We could also enforce per-user limits.
        // For now, we just create a session with a random ID and 30-minute expiry.

        var repo = await db.Repositories.FindAsync(repoId);
        if (repo is null)
            return Results.Json(new { error = "No repo found" }, statusCode: 404);
        
        var session = new IngestSession
        {
            Id = Guid.NewGuid(),
            RepoId = repo.Id,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            State = IngestSessionState.Created
        };
        
        db.IngestSessions.Add(session);
        await db.SaveChangesAsync();
        
        // return new session ID and expiry time (30 minutes from now)
        return Results.Json(new CreateIngestSessionResponse(session.Id, session.ExpiresAt), statusCode: 201);
    }
    
    private static async Task<IResult> GetSessionStatsAsync(Guid repoId, Guid sessionId, BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        var ingestSession = await db.IngestSessions.FindAsync(sessionId);
        if (ingestSession == null || ingestSession.RepoId != repoId)
            return Results.NotFound();
        
        var stats = new IngestSessionStatsDto(ingestSession.Id, (short)ingestSession.State, ingestSession.StartedAt, 0);
        
        return Results.Json(stats);
    }
    
    private static async Task<IResult> GetMissingFileDefinitionsAsync(Guid repoId, Guid sessionId, HttpRequest request, BinStashDbContext db)
    {
        try
        {
            var fileDefinitionChecksums = (await ChecksumCompressor.TransposeDecompressAsync(request.Body)).Select(x => new Hash32(x)).ToList();

            var ingestSession = await db.IngestSessions.FindAsync(sessionId);
            var repo = await db.Repositories.FindAsync(repoId);
            if (repo == null || ingestSession == null)
                return Results.NotFound();
            
            var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
            if (store == null)
                return Results.NotFound();
            
            // Return a list of all file definitions that are in the request but not in the database with the store id
            if (!fileDefinitionChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");
            
            var knownChecksums = db.FileDefinitions
                .Where(c => c.ChunkStoreId == store.Id && fileDefinitionChecksums.Contains(c.Checksum))
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
    
    private static async Task<IResult> UploadFileDefinitionsBatchAsync(Guid repoId, BinStashDbContext db, HttpRequest request)
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
        
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null)
            return Results.NotFound();
            
        var storeMeta = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (storeMeta == null)
            return Results.NotFound();
        
        var fileHashes = fileDefinitions.Keys.ToList();
        var existingFiles = db.FileDefinitions.Where(x => x.ChunkStoreId == storeMeta.Id && fileHashes.Contains(x.Checksum)).Select(x => x.Checksum).ToList();
        
        var store = new ChunkStore(storeMeta.Name, storeMeta.Type, storeMeta.LocalPath, new LocalFolderObjectStorage(storeMeta.LocalPath));

        foreach (var fileDefinition in fileDefinitions.Where(x => !existingFiles.Contains(x.Key)))
        {
            var entry = new FileDefinition
            {
                Checksum = fileDefinition.Key,
                ChunkStoreId = storeMeta.Id,
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
    
    private static async Task<IResult> GetMissingChunksAsync(Guid repoId, Guid sessionId, HttpRequest request, BinStashDbContext db)
    {
        try
        {
            var chunkChecksums = (await ChecksumCompressor.TransposeDecompressAsync(request.Body)).Select(x => new Hash32(x)).ToArray();
            
            var ingestSession = await db.IngestSessions.FindAsync(sessionId);
            var repo = await db.Repositories.FindAsync(repoId);
            if (repo == null || ingestSession == null)
                return Results.NotFound();
            
            var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
            if (store == null)
                return Results.NotFound();
            
            // Return a list of all chunks that are in the dto but not in the database with the store id
            if (!chunkChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");
            
            var knownChecksums = db.Chunks
                .Where(c => c.ChunkStoreId == sessionId && ((IEnumerable<Hash32>)chunkChecksums).Contains(c.Checksum))
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

    private static async Task<IResult> UploadChunkAsync(Guid repoId, Guid sessionId, string chunkChecksum, BinStashDbContext db, Stream chunkStream)
    {
        var checksum = Hash32.FromHexString(chunkChecksum);
        
        var ingestSession = await db.IngestSessions.FindAsync(sessionId);
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null || ingestSession == null)
            return Results.NotFound();
            
        var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            return Results.NotFound();
            
        store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
            
        using var ms = new MemoryStream();
        await chunkStream.CopyToAsync(ms);
        ms.Position = 0;

        if (db.Chunks.Any(c => c.ChunkStoreId == repoId && c.Checksum == checksum)) return Results.Ok();
        var (success, bytesWritten) = await store.StoreChunkAsync(chunkChecksum, ms.ToArray());
        if (!success) return Results.Problem();
        db.Chunks.Add(new Chunk
        {
            Checksum = checksum,
            ChunkStoreId = repoId,
            Length = Convert.ToInt32(ms.Length),
            CompressedLength = bytesWritten
        });
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static async Task<IResult> UploadChunksBatchAsync(Guid repoId, List<ChunkUploadDto> chunks, BinStashDbContext db, HttpRequest request)
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
        
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null)
            return Results.NotFound();
            
        var storeMeta = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
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
            .Where(c => c.ChunkStoreId == repoId && ((IEnumerable<Hash32>)checksums).Contains(c.Checksum))
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
                ChunkStoreId = repoId,
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
    
    private static async Task<IResult> FinalizeIngestSessionAsync(Guid repoId, Guid sessionId, BinStashDbContext db, HttpRequest request)
    {
        var ingestSession = await db.IngestSessions.FindAsync(sessionId);
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null || ingestSession == null)
            return Results.NotFound();
        
        if (ingestSession.State == IngestSessionState.Completed)
            return Results.Ok();
        
        if (ingestSession.State == IngestSessionState.Completed || ingestSession.State == IngestSessionState.Failed || ingestSession.State == IngestSessionState.Aborted || ingestSession.State == IngestSessionState.Expired || ingestSession.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.BadRequest("Ingest session is not active.");
        
        if (ingestSession.State == IngestSessionState.Created)
            ingestSession.State = IngestSessionState.InProgress;
        
        if (!request.HasFormContentType)
            return Results.BadRequest("Content-Type must be multipart/form-data.");
        
        var form = await request.ReadFormAsync();
        
        var file = form.Files.GetFile("releaseDefinition");
        if (file == null || file.Length == 0)
            return Results.BadRequest("Missing or empty release definition file.");
        
        var contentType = file.ContentType;
        if (contentType is not "application/x-bs-rdef" )
            return Results.BadRequest("Unsupported Content-Type.");

        var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            return Results.NotFound("Chunk store not found.");

        var releaseId = Guid.CreateVersion7();
        await using var stream = file.OpenReadStream();

        var releasePackage = await ReleasePackageSerializer.DeserializeAsync(stream);
        
        if (db.Releases.Any(r => r.RepoId == repo.Id && r.Version == releasePackage.Version))
            return Results.Conflict($"A release with version '{releasePackage.Version}' already exists for this repository.");

        var createdAt = DateTimeOffset.UtcNow;
        
        releasePackage.CreatedAt = createdAt;
        releasePackage.ReleaseId = releaseId.ToString();
        releasePackage.RepoId = repo.Id.ToString();
        
        await using var releasePackageStream = new MemoryStream();
        await ReleasePackageSerializer.SerializeAsync(releasePackageStream, releasePackage);
        var releasePackageData = releasePackageStream.ToArray();
        var hash = new Hash32(Blake3.Hasher.Hash(releasePackageData).AsSpan());

        var release = new Release
        {
            Id = releaseId,
            Version = releasePackage.Version,
            CreatedAt = createdAt,
            Notes = releasePackage.Notes,
            RepoId = repo.Id,
            Repository = repo,
            ReleaseDefinitionChecksum = hash,
            CustomProperties = releasePackage.CustomProperties.Count > 0 ? releasePackage.CustomProperties.ToJson() : null,
            SerializerVersion = ReleasePackageSerializer.Version
        };
        
        await db.Releases.AddAsync(release);

        var chunkStore = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
        await chunkStore.StoreReleasePackageAsync(releasePackageData);
        
        ingestSession.MetadataSize =+ releasePackageData.Length;
        
        ingestSession.State = IngestSessionState.Completed;
        ingestSession.CompletedAt = DateTimeOffset.UtcNow;

        var chunkHashes = releasePackage.Chunks.Select(ci => ci.Checksum).Select(x => new Hash32(x)).ToList();
        var rawSize = await db.Chunks.Where(c => chunkHashes.Contains(c.Checksum)).SumAsync(x => x.Length);
        
        var releaseMetrics = new ReleaseMetrics
        {
            ReleaseId = release.Id,
            IngestSessionId = ingestSession.Id,
            CreatedAt = release.CreatedAt,
            ChunksInRelease = releasePackage.Chunks.Count,
            NewChunks = ingestSession.ChunksSeenNew,
            TotalUncompressedSize = (ulong)rawSize,
            NewCompressedBytes = ingestSession.DataSizeUnique,
            MetaBytesFull = ingestSession.MetadataSize,
            MetaBytesFullDiff = 0, // Set if we save a patch/diff instead of the full release definition
            ComponentsInRelease = releasePackage.Components.Count,
            FilesInRelease = releasePackage.Components.Sum(c => c.Files.Count)
        };

        await db.ReleaseMetrics.AddAsync(releaseMetrics);

        await db.SaveChangesAsync();

        return Results.Created($"/api/releases/{releaseId}", null);
    }
}