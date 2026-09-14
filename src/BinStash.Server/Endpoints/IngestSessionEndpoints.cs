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
using BinStash.Contracts.Release;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Compression;
using BinStash.Core.Entities;
using BinStash.Core.Extensions;
using BinStash.Core.Serialization;
using BinStash.Core.Serialization.Utils;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage.FileDefinition;
using BinStash.Server.Billing;
using BinStash.Server.Context;
using BinStash.Server.Extensions;
using BinStash.Server.Services.Billing;
using BinStash.Server.Services.ChunkStores;
using BinStash.Core.Traffic;
using BinStash.Server.Services.Usage;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ZstdNet;

using BinStash.Core.Auditing;
using BinStash.Server.Services.Releases;

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
            .Produces(402)
            .RequireRepoPermission(RepositoryPermission.Write)
            // Session creation is the admission point for writes, and the quota check below lives
            // on it. Throttling it is what stops a client from working around a per-session limit
            // by opening sessions in a loop.
            .RequireRateLimiting(RateLimitPolicies.IngestSessionCreation);
        
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

    private static async Task<IResult> CreateIngestSessionAsync(Guid tenantId, Guid repoId, CreateIngestSessionRequest? body, BinStashDbContext db, TenantQuotaGuard quota, CancellationToken ct)
    {
        // Enforce quota before admitting the write. Covers ingest, the storage switch, and the
        // storage ceiling as it stands right now; finalize checks the ceiling again once the
        // release's real size is known.
        var admission = await quota.CheckIngestAsync(tenantId, ct);
        if (!admission.IsAllowed)
            return admission.ToProblem();

        // If we have authentication, we can link the session to a user. We could also enforce per-user limits.
        // For now, we just create a session with a random ID and 30-minute expiry.

        if (body is null)
            return Results.BadRequest("Request body is required.");
        
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo is null)
            return Results.Problem("No repo found", statusCode: 404);
        
        if (db.Releases.Any(r => r.RepoId == repoId && r.Version == body.IntendedRelease))
            return Results.Problem("A release with the intended version already exists for this repository.", statusCode: 400);
        
        var session = new IngestSession
        {
            Id = Guid.NewGuid(),
            RepoId = repo.Id,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            State = IngestSessionState.Created,
            IntendedRelease = body.IntendedRelease
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
            var fileDefinitionChecksums = await ChecksumCompressor.TransposeDecompressHashesAsync(request.Body);

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
            
            var knownChecksums = await db.FileDefinitions
                .Where(c => c.ChunkStoreId == store.Id && fileDefinitionChecksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToListAsync();
            
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
    
    private static async Task<IResult> UploadFileDefinitionsBatchAsync(Guid repoId, BinStashDbContext db, IChunkStoreService chunkStoreService, IGcQuarantineService quarantine, HttpRequest request)
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
        var chunkChecksums = await ChecksumCompressor.TransposeDecompressHashesAsync(decompressionStream);
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
                chunks.Add(chunkChecksums[chunkIndex]);
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
        var addedFileDefinitions = new List<Hash32>();
        
        foreach (var fileDefinition in fileDefinitions.Where(x => !existingFiles.Contains(x.Key)))
        {
            var record = new FileDefinitionRecord
            {
                FileHash    = fileDefinition.Key,
                FileLength  = fileDefinition.Value.Length,
                ChunkHashes = fileDefinition.Value.Chunks
            };

            var blob = record.Serialize();

            var storeFileDefinitionResult = await chunkStoreService.StoreFileDefinitionAsync(storeMeta, blob);
            
            if (!storeFileDefinitionResult.Success)
                return Results.Problem($"Failed to store file definition ({fileDefinition.Key.ToHexString()}) in chunk store.");
            
            ingestSession.FilesSeenUnique++;

            if (storeFileDefinitionResult.WasNew)
            {
                ingestSession.FilesSeenNew++;
                ingestSession.MetadataSize += storeFileDefinitionResult.BytesWritten;
            }

            // Owed whenever the catalogue said the definition was missing, even if the blob was
            // already stored — after a crash between the two writes, or because garbage collection
            // quarantined it (row dropped on purpose, bytes kept).
            var entry = new FileDefinition
            {
                Checksum     = fileDefinition.Key,
                ChunkStoreId = storeMeta.Id,
                Length       = fileDefinition.Value.Length,
            };
            
            db.FileDefinitions.Add(entry);
            addedFileDefinitions.Add(entry.Checksum);
        }
        
        ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        // Restoring the catalogue rows un-quarantines the definitions, so a later reclaim cannot
        // delete bytes this session now depends on.
        if (addedFileDefinitions.Count > 0)
            await quarantine.ResurrectAsync(storeMeta.Id, GcObjectCategory.FileDefinition, addedFileDefinitions);

        await db.SaveChangesAsync();
        
        return Results.Created();
    }
    
    private static async Task<IResult> GetMissingChunksAsync(Guid repoId, Guid sessionId, HttpRequest request, BinStashDbContext db)
    {
        try
        {
            var chunkChecksums = (await ChecksumCompressor.TransposeDecompressHashesAsync(request.Body)).ToArray();

            var ingestSession = await db.IngestSessions.FindAsync(sessionId);
            var repo = await db.Repositories.FindAsync(repoId);
            if (repo == null || ingestSession == null)
                return Results.NotFound();

            var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
            if (store == null)
                return Results.NotFound();

            if (!chunkChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");

            var knownChecksums = await db.Chunks
                .Where(c => c.ChunkStoreId == store.Id && chunkChecksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToListAsync();

            var missingChecksums = chunkChecksums.Except(knownChecksums).ToList();

            if (!missingChecksums.Any())
                return Results.Bytes(ChecksumCompressor.TransposeCompress([]), "application/octet-stream");

            return Results.Bytes(
                ChecksumCompressor.TransposeCompress(missingChecksums.Select(x => x.GetBytes()).ToList()),
                "application/octet-stream");
        }
        catch (Exception)
        {
            return Results.BadRequest("Invalid request body.");
        }
    }

    private static async Task<IResult> UploadChunkAsync(Guid repoId, Guid sessionId, string chunkChecksum, BinStashDbContext db, Stream chunkStream, IChunkStoreService chunkStoreService, IGcQuarantineService quarantine)
    {
        var checksum = Hash32.FromHexString(chunkChecksum);
        
        var ingestSession = await db.IngestSessions.FindAsync(sessionId);
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null || ingestSession == null)
            return Results.NotFound();
            
        var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            return Results.NotFound();
        
        using var ms = new MemoryStream();
        await chunkStream.CopyToAsync(ms);
        ms.Position = 0;

        if (await db.Chunks.AnyAsync(c => c.ChunkStoreId == store.Id && c.Checksum == checksum)) return Results.Ok();
        var (success, _, bytesWritten) = await chunkStoreService.StoreChunkAsync(store, chunkChecksum, ms.ToArray());
        if (!success) return Results.Problem();

        // The catalogue said the chunk was missing, so the row is owed even when the bytes were
        // already present — which happens after a crash between the two writes, or when garbage
        // collection quarantined the object (the row is dropped on purpose; the bytes are not).
        db.Chunks.Add(new Chunk
        {
            Checksum = checksum,
            ChunkStoreId = store.Id,
            Length = Convert.ToInt32(ms.Length),
            CompressedLength = bytesWritten
        });

        // Restoring the row un-quarantines the chunk. Without this the tombstone survives and a
        // later reclaim would delete bytes this session now depends on.
        await quarantine.ResurrectAsync(store.Id, GcObjectCategory.Chunk, [checksum]);
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static async Task<IResult> UploadChunksBatchAsync(Guid repoId, List<ChunkUploadDto> chunks, BinStashDbContext db, IChunkStoreService chunkStoreService, IGcQuarantineService quarantine, ITrafficRecorder trafficRecorder, HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-Ingest-Session-Id", out var ingestIdHeaders) ||
            !Guid.TryParse(ingestIdHeaders.First(), out var ingestId))
        {
            return Results.BadRequest("Missing or invalid X-Ingest-Session-Id header.");
        }

        var ingestSession = await db.IngestSessions.FindAsync(ingestId);
        if (ingestSession == null)
            return Results.BadRequest("Invalid X-Ingest-Session-Id header value.");

        if (ingestSession.State == IngestSessionState.Completed ||
            ingestSession.State == IngestSessionState.Failed ||
            ingestSession.State == IngestSessionState.Aborted ||
            ingestSession.State == IngestSessionState.Expired ||
            ingestSession.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Results.BadRequest("Ingest session is not active.");
        }

        if (ingestSession.State == IngestSessionState.Created)
            ingestSession.State = IngestSessionState.InProgress;

        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null)
            return Results.NotFound();

        var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            return Results.NotFound();

        if (chunks.Count == 0)
            return Results.BadRequest("No chunks provided.");

        var uniqueChunks = chunks
            .GroupBy(c => c.Checksum, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Hex = g.Key,
                Hash = Hash32.FromHexString(g.Key),
                g.First().Data
            })
            .ToList();

        ingestSession.ChunksSeenTotal += chunks.Count;
        ingestSession.ChunksSeenUnique += uniqueChunks.Count;

        // The REST upload path carries the same payloads as the gRPC one and has to be counted
        // too, or an instance whose clients use it would show no ingress at all.
        trafficRecorder.RecordIngress(repo.TenantId, chunks.Sum(c => (long)(c.Data?.Length ?? 0)));

        var checksumArray = uniqueChunks.Select(c => c.Hash).ToArray();

        var knownChecksums = (await db.Chunks
                .Where(c => c.ChunkStoreId == store.Id && checksumArray.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToListAsync())
            .ToHashSet();

        var candidateChunks = uniqueChunks
            .Where(c => !knownChecksums.Contains(c.Hash))
            .ToList();

        var newlyWrittenCompressedBytes = new ConcurrentDictionary<Hash32, int>();
        var newlyWrittenLogicalBytes = new ConcurrentDictionary<Hash32, int>();
        var storedCompressedBytes = new ConcurrentDictionary<Hash32, int>();

        var results = await Task.WhenAll(candidateChunks.Select(async chunk =>
        {
            var actualHash = new Hash32(Blake3.Hasher.Hash(chunk.Data).AsSpan());
            if (actualHash != chunk.Hash)
                return false;

            var (success, wasNew, bytesWritten) = await chunkStoreService.StoreChunkAsync(store, chunk.Hex, chunk.Data);
            if (!success)
                return false;

            // Recorded for every candidate, because every candidate is owed a catalogue row; the
            // wasNew flag separates "this ingest added data" from "this ingest healed a row".
            storedCompressedBytes[chunk.Hash] = bytesWritten;
            if (wasNew)
            {
                newlyWrittenCompressedBytes[chunk.Hash] = bytesWritten;
                newlyWrittenLogicalBytes[chunk.Hash] = chunk.Data.Length;
            }

            return true;
        }));

        if (results.Any(r => !r))
            return Results.Problem("Some chunks failed checksum or storage.");

        var chunksToAdd = candidateChunks
            .Where(chunk => storedCompressedBytes.ContainsKey(chunk.Hash))
            .Select(chunk => new Chunk
            {
                Checksum = chunk.Hash,
                ChunkStoreId = repo.ChunkStoreId,
                Length = chunk.Data.Length,
                CompressedLength = storedCompressedBytes[chunk.Hash]
            })
            .ToList();

        ingestSession.ChunksSeenNew += newlyWrittenCompressedBytes.Count;
        ingestSession.NewUniqueLogicalBytes += newlyWrittenLogicalBytes.Values.Sum();
        ingestSession.NewCompressedBytes += newlyWrittenCompressedBytes.Values.Sum();
        ingestSession.LastUpdatedAt = DateTimeOffset.UtcNow;
        ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        if (chunksToAdd.Count > 0)
        {
            db.Chunks.AddRange(chunksToAdd);

            await quarantine.ResurrectAsync(
                store.Id, GcObjectCategory.Chunk,
                chunksToAdd.Select(static c => c.Checksum).ToList());

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is Npgsql.PostgresException pg &&
                pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
            {
                db.ChangeTracker.Clear();

                ingestSession = await db.IngestSessions.FindAsync(ingestId);
                if (ingestSession != null)
                {
                    ingestSession.LastUpdatedAt = DateTimeOffset.UtcNow;
                    ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
                    await db.SaveChangesAsync();
                }
            }
        }
        else
        {
            await db.SaveChangesAsync();
        }

        return Results.Ok();
    }
    
    private static async Task<IResult> FinalizeIngestSessionAsync(Guid repoId, Guid sessionId, BinStashDbContext db, IChunkStoreService chunkStoreService, IGcQuarantineService quarantine, IAuditLogWriter audit, TenantQuotaGuard quota, TenantFootprintRefreshQueue footprintRefresh, HttpRequest request)
    {
        var ingestSession = await db.IngestSessions.FindAsync(sessionId);
        var repo = await db.Repositories.FindAsync(repoId);
        if (repo == null || ingestSession == null)
            return Results.NotFound();

        if (ingestSession.State == IngestSessionState.Completed)
            return Results.Ok();

        if (ingestSession.State == IngestSessionState.Completed ||
            ingestSession.State == IngestSessionState.Failed ||
            ingestSession.State == IngestSessionState.Aborted ||
            ingestSession.State == IngestSessionState.Expired ||
            ingestSession.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Results.BadRequest("Ingest session is not active.");
        }

        if (ingestSession.State == IngestSessionState.Created)
            ingestSession.State = IngestSessionState.InProgress;

        if (!request.HasFormContentType)
            return Results.BadRequest("Content-Type must be multipart/form-data.");

        var form = await request.ReadFormAsync();

        var file = form.Files.GetFile("releaseDefinition");
        if (file == null || file.Length == 0)
            return Results.BadRequest("Missing or empty release definition file.");

        if (file.ContentType is not "application/x-bs-rdef")
            return Results.BadRequest("Unsupported Content-Type.");

        var store = await db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            return Results.NotFound("Chunk store not found.");

        var releaseId = Guid.CreateVersion7();

        await using var stream = file.OpenReadStream();
        var (releasePackage, _) = await ReleasePackageSerializer.DeserializeAsync(stream);

        if (ingestSession.IntendedRelease != null && !string.Equals(ingestSession.IntendedRelease, releasePackage.Version, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest($"Release version '{releasePackage.Version}' does not match the intended version '{ingestSession.IntendedRelease}' for this ingest session.");
        
        if (await db.Releases.AnyAsync(r => r.RepoId == repo.Id && r.Version == releasePackage.Version))
            return Results.Conflict($"A release with version '{releasePackage.Version}' already exists for this repository.");

        var createdAt = DateTimeOffset.UtcNow;

        releasePackage.CreatedAt = createdAt;
        releasePackage.ReleaseId = releaseId.ToString();
        releasePackage.RepoId = repo.Id.ToString();

        await using var releasePackageStream = new MemoryStream();
        _ = await ReleasePackageSerializer.SerializeAsync(releasePackageStream, releasePackage);
        var releasePackageData = releasePackageStream.ToArray();
        var releasePackageHash = new Hash32(Blake3.Hasher.Hash(releasePackageData).AsSpan());

        var release = new Release
        {
            Id = releaseId,
            Version = releasePackage.Version,
             CreatedAt = createdAt,
             Notes = releasePackage.Notes,
             RepoId = repo.Id,
             Repository = repo,
             ReleaseDefinitionChecksum = releasePackageHash,
#pragma warning disable IL2026, IL3050 // ToJson uses reflection; Server is not AOT-published
             CustomProperties = releasePackage.CustomProperties.Count > 0 ? releasePackage.CustomProperties.ToJson() : null,
#pragma warning restore IL2026, IL3050
             SerializerVersion = ReleasePackageSerializer.Version
        };

        await chunkStoreService.StoreReleasePackageAsync(store, releasePackageData);

        ingestSession.MetadataSize += releasePackageData.Length;
        ingestSession.LastUpdatedAt = DateTimeOffset.UtcNow;
        ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        var outputArtifacts = releasePackage.OutputArtifacts;

        var allReferencedFileHashes = CollectReferencedFileHashes(outputArtifacts);
        var distinctFileHashes = allReferencedFileHashes.Distinct().ToList();

        // A release may only be committed once every object it names is live in the catalogue.
        // Two things can make that untrue at exactly this point, and they need opposite answers:
        //
        //   * Garbage collection quarantined an object after this session was told it already
        //     had it. The bytes are still there — quarantine only hides the catalogue row — so
        //     lifting the tombstone repairs the reference completely.
        //   * The object is genuinely gone. Committing anyway would produce a release that can
        //     never be downloaded, and the failure would surface much later, to someone else.
        //
        // Resurrection is committed on its own before the check, so the check reads the same
        // catalogue any later download will. If the rest of finalize then fails, the worst
        // outcome is some objects left un-quarantined, which the next collection undoes.
        await quarantine.ResurrectAsync(store.Id, GcObjectCategory.FileDefinition, distinctFileHashes);
        await db.SaveChangesAsync();

        var missingFileDefinitions = await FindMissingFileDefinitionsAsync(db, store.Id, distinctFileHashes);
        if (missingFileDefinitions.Count > 0)
        {
            return Results.Conflict(
                $"{missingFileDefinitions.Count} file definition(s) referenced by this release are no longer " +
                $"present in the chunk store (for example {missingFileDefinitions[0].ToHexString()}). " +
                "Start a new ingest session and re-upload; the missing content will be accepted as new.");
        }

        ulong totalLogicalBytes = 0;
        foreach (var artifact in outputArtifacts)
        {
            totalLogicalBytes += ReleaseMetricsCalculator.CalculateLogicalArtifactSize(artifact);
        }

        ingestSession.TotalLogicalBytes = (long)totalLogicalBytes;

        // The authoritative storage check. Session creation checked the ceiling against usage as
        // it stood then; this is the first point at which the size of *this* release is known, so
        // it is the only place the ceiling can actually be held. Checked before the integrity
        // work below because there is no reason to pay for it on a release that cannot land.
        var storageAdmission = await quota.CheckStorageCommitAsync(repo.TenantId, (long)totalLogicalBytes);
        if (!storageAdmission.IsAllowed)
            return storageAdmission.ToProblem();

        var fileDefinitionBytesByChecksum = distinctFileHashes.Count == 0
            ? new Dictionary<string, byte[]>()
            : await chunkStoreService.RetrieveFileDefinitionsAsync(
                store,
                distinctFileHashes.Select(k => k.ToHexString()).ToArray());

        var releaseUniqueChunks = new HashSet<Hash32>();
        foreach (var (_, blob) in fileDefinitionBytesByChecksum)
        {
            foreach (var chunkHash in FileDefinitionRecord.Deserialize(blob).ChunkHashes)
                releaseUniqueChunks.Add(chunkHash);
        }

        // Same gate one level down: the file definitions resolved, but each one is only useful
        // if every chunk it names is still live.
        var referencedChunks = releaseUniqueChunks.ToList();
        await quarantine.ResurrectAsync(store.Id, GcObjectCategory.Chunk, referencedChunks);
        await db.SaveChangesAsync();

        var missingChunks = await FindMissingChunksAsync(db, store.Id, referencedChunks);
        if (missingChunks.Count > 0)
        {
            return Results.Conflict(
                $"{missingChunks.Count} chunk(s) referenced by this release are no longer present in the " +
                $"chunk store (for example {missingChunks[0].ToHexString()}). Start a new ingest session and " +
                "re-upload; the missing content will be accepted as new.");
        }

        // Only now is the release known to be complete and servable. Marking the session done any
        // earlier would strand a client that hits the integrity check below: the session it would
        // need to retry on is already closed.
        ingestSession.State = IngestSessionState.Completed;
        ingestSession.CompletedAt = DateTimeOffset.UtcNow;

        await db.Releases.AddAsync(release);

        var fileArtifactsInRelease = outputArtifacts.Count(x => x.Kind == OutputArtifactKind.File);
        var componentCountInRelease = outputArtifacts
            .Select(x => x.ComponentName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var releaseMetrics = new ReleaseMetrics
        {
            ReleaseId = release.Id,
            IngestSessionId = ingestSession.Id,
            CreatedAt = release.CreatedAt,

            ChunksInRelease = releaseUniqueChunks.Count,
            NewChunks = (int)ingestSession.ChunksSeenNew,

            TotalLogicalBytes = totalLogicalBytes,
            NewUniqueLogicalBytes = ingestSession.NewUniqueLogicalBytes,
            NewCompressedBytes = ingestSession.NewCompressedBytes,

            MetaBytesFull = ingestSession.MetadataSize,
            MetaBytesFullDiff = 0,

            ComponentsInRelease = componentCountInRelease,
            FilesInRelease = fileArtifactsInRelease,

            IncrementalCompressionRatio =
                ingestSession.NewCompressedBytes > 0
                    ? (double)ingestSession.NewUniqueLogicalBytes / ingestSession.NewCompressedBytes
                    : 1.0,

            IncrementalDeduplicationRatio =
                ingestSession.NewUniqueLogicalBytes > 0
                    ? (double)totalLogicalBytes / ingestSession.NewUniqueLogicalBytes
                    : 1.0,

            IncrementalEffectiveRatio =
                (ingestSession.NewCompressedBytes + ingestSession.MetadataSize) > 0
                    ? (double)totalLogicalBytes / (ingestSession.NewCompressedBytes + ingestSession.MetadataSize)
                    : 1.0,

            CompressionSavedBytes =
                Math.Max(0, ingestSession.NewUniqueLogicalBytes - ingestSession.NewCompressedBytes),

            DeduplicationSavedBytes =
                Math.Max(0, (long)totalLogicalBytes - ingestSession.NewUniqueLogicalBytes),

            NewDataPercent =
                totalLogicalBytes > 0
                    ? (double)ingestSession.NewUniqueLogicalBytes / totalLogicalBytes * 100.0
                    : 0.0
        };

        await db.ReleaseMetrics.AddAsync(releaseMetrics);
        await db.SaveChangesAsync();

        // Provenance: this is the only place a release enters the store, and it is reached
        // over REST/gRPC rather than GraphQL, so it needs its own audit write. Deliberately
        // records only the tenant's own quantities — the deduplicated/compressed figures
        // here are a property of the shared chunk store (see TenantUsageGql).
        await audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.ReleasePublished,
            TenantId = repo.TenantId,
            TargetType = nameof(Release),
            TargetId = releaseId.ToString(),
            TargetName = $"{repo.Name} {releasePackage.Version}",
            Metadata = new Dictionary<string, object?>
            {
                ["repositoryId"] = repo.Id,
                ["version"] = releasePackage.Version,
                ["logicalBytes"] = (long)totalLogicalBytes,
                ["files"] = releaseMetrics.FilesInRelease,
                ["ingestSessionId"] = sessionId
            }
        });

        // The tenant now holds more than their last snapshot says. Queue the recomputation so
        // their plan limit is checked against a current figure rather than a day-old one.
        footprintRefresh.Enqueue(repo.TenantId);

        return Results.Created($"/api/releases/{releaseId}", null);
    }
    
    /// <summary>
    /// How many hashes go into one <c>IN (...)</c> predicate. A release routinely references
    /// hundreds of thousands of chunks, and PostgreSQL caps a statement at 65535 parameters.
    /// </summary>
    private const int IntegrityCheckBatchSize = 2000;

    private static async Task<List<Hash32>> FindMissingFileDefinitionsAsync(BinStashDbContext db, Guid chunkStoreId, IReadOnlyCollection<Hash32> hashes)
    {
        var missing = new List<Hash32>();

        foreach (var batch in hashes.Chunk(IntegrityCheckBatchSize))
        {
            var present = await db.FileDefinitions
                .Where(f => f.ChunkStoreId == chunkStoreId && batch.Contains(f.Checksum))
                .Select(f => f.Checksum)
                .ToHashSetAsync();

            missing.AddRange(batch.Where(h => !present.Contains(h)));
        }

        return missing;
    }

    private static async Task<List<Hash32>> FindMissingChunksAsync(BinStashDbContext db, Guid chunkStoreId, IReadOnlyCollection<Hash32> hashes)
    {
        var missing = new List<Hash32>();

        foreach (var batch in hashes.Chunk(IntegrityCheckBatchSize))
        {
            var present = await db.Chunks
                .Where(c => c.ChunkStoreId == chunkStoreId && batch.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToHashSetAsync();

            missing.AddRange(batch.Where(h => !present.Contains(h)));
        }

        return missing;
    }

    private static List<Hash32> CollectReferencedFileHashes(IEnumerable<OutputArtifact> outputArtifacts)
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

}