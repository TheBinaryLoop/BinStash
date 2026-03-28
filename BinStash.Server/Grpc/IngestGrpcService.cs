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

using System.Text;
using BinStash.Contracts.Hashing;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Compression;
using BinStash.Core.Entities;
using BinStash.Core.Serialization.Utils;
using BinStash.Grpc;
using BinStash.Infrastructure.Data;
using BinStash.Server.Auth;
using BinStash.Server.Services.ChunkStores;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZstdNet;

namespace BinStash.Server.Grpc;

public sealed class IngestGrpcService : IngestService.IngestServiceBase
{
    private readonly BinStashDbContext _db;
    private readonly IChunkStoreService _chunkStoreService;
    private readonly IAuthorizationService _authorizationService;

    public IngestGrpcService(BinStashDbContext db, IChunkStoreService chunkStoreService, IAuthorizationService authorizationService)
    {
        _db = db;
        _chunkStoreService = chunkStoreService;
        _authorizationService = authorizationService;
    }

    [Authorize]
    public override async Task<UploadFileDefinitionsReply> UploadFileDefinitions(IAsyncStreamReader<UploadFileDefinitionsRequest> requestStream, ServerCallContext context)
    {
        var http = context.GetHttpContext();
        var user = http.User;

        if (!user.Identity?.IsAuthenticated ?? true)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Authentication required."));

        var ingestHeader = context.RequestHeaders.FirstOrDefault(x => x.Key == "x-ingest-session-id")?.Value;
        if (!Guid.TryParse(ingestHeader, out var ingestId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing or invalid x-ingest-session-id metadata."));

        var repoHeader = context.RequestHeaders.FirstOrDefault(x => x.Key == "x-repo-id")?.Value;
        if (!Guid.TryParse(repoHeader, out var repoId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing or invalid x-repo-id metadata."));

        var ingestSession = await _db.IngestSessions.FindAsync(ingestId);
        if (ingestSession == null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid ingest session id."));

        if (ingestSession.State is IngestSessionState.Completed
            or IngestSessionState.Failed
            or IngestSessionState.Aborted
            or IngestSessionState.Expired
            || ingestSession.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Ingest session is not active."));
        }

        if (ingestSession.State == IngestSessionState.Created)
            ingestSession.State = IngestSessionState.InProgress;

        var repo = await _db.Repositories.FindAsync(repoId);
        if (repo == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Repository not found."));

        var authResult = await AuthChecker.CheckRepositoryPermissionAsync(http.User, _authorizationService, repo.TenantId, repo.Id, RepositoryPermission.Write);

        if (!authResult.Succeeded)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Insufficient permissions for repository."));

        var store = await _db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Chunk store not found."));

        var filesSeenTotal = 0;
        var filesSeenUnique = 0;
        var filesWrittenNew = 0;
        var metadataBytesWritten = 0;

        var seenInRequest = new HashSet<Hash32>();
        var fileDefinitionsToAdd = new List<FileDefinition>();

        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var msg = requestStream.Current;
            var batchPayload = msg.Payload.Memory.ToArray();

            var batch = await DecodeFileDefinitionsBatchAsync(batchPayload, context.CancellationToken);

            filesSeenTotal += batch.Count;

            var fileHashes = batch.Keys.ToList();

            var existingFiles = await _db.FileDefinitions
                .Where(x => x.ChunkStoreId == store.Id && fileHashes.Contains(x.Checksum))
                .Select(x => x.Checksum)
                .ToListAsync(context.CancellationToken);

            var existingSet = existingFiles.ToHashSet();

            foreach (var fileDefinition in batch)
            {
                var fileHash = fileDefinition.Key;
                var fileLength = fileDefinition.Value.Length;
                var chunkList = fileDefinition.Value.Chunks;

                if (!seenInRequest.Add(fileHash))
                    continue;

                filesSeenUnique++;

                if (existingSet.Contains(fileHash))
                    continue;

                var compressedChunkList = ChecksumCompressor.TransposeCompress(
                    chunkList.Select(x => x.GetBytes()).ToList());

                var storeResult = await _chunkStoreService.StoreFileDefinitionAsync(
                    store,
                    fileHash,
                    compressedChunkList);

                if (!storeResult.Success)
                    throw new RpcException(new Status(StatusCode.Internal, $"Failed to store file definition ({fileHash.ToHexString()}) in chunk store."));

                metadataBytesWritten += storeResult.BytesWritten;
                filesWrittenNew++;

                fileDefinitionsToAdd.Add(new FileDefinition
                {
                    Checksum = fileHash,
                    ChunkStoreId = store.Id,
                    Length = fileLength
                });
            }
        }

        ingestSession.FilesSeenTotal += filesSeenTotal;
        ingestSession.FilesSeenUnique += filesSeenUnique;
        ingestSession.FilesSeenNew += filesWrittenNew;
        ingestSession.MetadataSize += metadataBytesWritten;
        ingestSession.LastUpdatedAt = DateTimeOffset.UtcNow;
        ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        if (fileDefinitionsToAdd.Count > 0)
            _db.FileDefinitions.AddRange(fileDefinitionsToAdd);

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();

            var reloaded = await _db.IngestSessions.FindAsync([ingestId], context.CancellationToken);
            if (reloaded != null)
            {
                reloaded.LastUpdatedAt = DateTimeOffset.UtcNow;
                reloaded.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
                await _db.SaveChangesAsync(context.CancellationToken);
            }
        }

        return new UploadFileDefinitionsReply
        {
            FilesSeenTotal = filesSeenTotal,
            FilesSeenUnique = filesSeenUnique,
            FilesWrittenNew = filesWrittenNew,
            MetadataBytesWritten = metadataBytesWritten
        };
    }

    [Authorize]
    public override async Task<UploadChunksReply> UploadChunks(IAsyncStreamReader<UploadChunkRequest> requestStream, ServerCallContext context)
    {
        var http = context.GetHttpContext();
        var user = http.User;
        
        if (!user.Identity?.IsAuthenticated ?? true)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Authentication required."));
        
        var ingestHeader = context.RequestHeaders.FirstOrDefault(x => x.Key == "x-ingest-session-id")?.Value;
        if (!Guid.TryParse(ingestHeader, out var ingestId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing or invalid x-ingest-session-id metadata."));
        
        var repoHeader = context.RequestHeaders.FirstOrDefault(x => x.Key == "x-repo-id")?.Value;
        if (!Guid.TryParse(repoHeader, out var repoId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing or invalid x-repo-id metadata."));

        var ingestSession = await _db.IngestSessions.FindAsync(ingestId);
        if (ingestSession == null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid ingest session id."));

        if (ingestSession.State is IngestSessionState.Completed
            or IngestSessionState.Failed
            or IngestSessionState.Aborted
            or IngestSessionState.Expired
            || ingestSession.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Ingest session is not active."));
        }

        if (ingestSession.State == IngestSessionState.Created)
            ingestSession.State = IngestSessionState.InProgress;
        
        var repo = await _db.Repositories.FindAsync(repoId);
        if (repo == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Repository not found."));

        var authResult = await AuthChecker.CheckRepositoryPermissionAsync(http.User, _authorizationService, repo.TenantId, repo.Id, RepositoryPermission.Write);

        if (!authResult.Succeeded)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Insufficient permissions for repository."));

        var store = await _db.ChunkStores.FindAsync(repo.ChunkStoreId);
        if (store == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Chunk store not found."));

        var seenInRequest = new HashSet<Hash32>();
        var newlyWrittenCompressedBytes = new Dictionary<Hash32, int>();
        var newlyWrittenLogicalBytes = new Dictionary<Hash32, int>();
        var chunksToAdd = new List<Chunk>();

        var chunksSeenTotal = 0;
        var chunksSeenUnique = 0;

        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var msg = requestStream.Current;
            chunksSeenTotal++;
            
            var hash = new Hash32(msg.Checksum.Span);

            if (!seenInRequest.Add(hash))
                continue;

            chunksSeenUnique++;

            var alreadyKnown = await _db.Chunks
                .AnyAsync(c => c.ChunkStoreId == store.Id && c.Checksum == hash, context.CancellationToken);

            if (alreadyKnown)
                continue;

            var data = msg.Data.Memory.ToArray();
            var actualHash = new Hash32(Blake3.Hasher.Hash(data).AsSpan());
            if (actualHash != hash)
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Checksum mismatch for {msg.Checksum}."));

            var (success, bytesWritten) = await _chunkStoreService.StoreChunkAsync(store, actualHash.ToHexString(), data);
            if (!success)
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to store chunk {msg.Checksum}."));

            if (bytesWritten > 0)
            {
                newlyWrittenCompressedBytes[hash] = bytesWritten;
                newlyWrittenLogicalBytes[hash] = data.Length;

                chunksToAdd.Add(new Chunk
                {
                    Checksum = hash,
                    ChunkStoreId = store.Id,
                    Length = data.Length,
                    CompressedLength = bytesWritten
                });
            }
        }

        ingestSession.ChunksSeenTotal += chunksSeenTotal;
        ingestSession.ChunksSeenUnique += chunksSeenUnique;
        ingestSession.ChunksSeenNew += chunksToAdd.Count;
        ingestSession.NewUniqueLogicalBytes += newlyWrittenLogicalBytes.Values.Sum();
        ingestSession.NewCompressedBytes += newlyWrittenCompressedBytes.Values.Sum();
        ingestSession.LastUpdatedAt = DateTimeOffset.UtcNow;
        ingestSession.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        if (chunksToAdd.Count > 0)
            _db.Chunks.AddRange(chunksToAdd);

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();

            var reloaded = await _db.IngestSessions.FindAsync([ingestId], context.CancellationToken);
            if (reloaded != null)
            {
                reloaded.LastUpdatedAt = DateTimeOffset.UtcNow;
                reloaded.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
                await _db.SaveChangesAsync(context.CancellationToken);
            }
        }

        return new UploadChunksReply
        {
            ChunksSeenTotal = chunksSeenTotal,
            ChunksSeenUnique = chunksSeenUnique,
            ChunksWrittenNew = chunksToAdd.Count,
            NewUniqueLogicalBytes = newlyWrittenLogicalBytes.Values.Sum(),
            NewCompressedBytes = newlyWrittenCompressedBytes.Values.Sum()
        };
    }
    
    private static async Task<Dictionary<Hash32, (List<Hash32> Chunks, long Length)>> DecodeFileDefinitionsBatchAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Hash32, (List<Hash32> Chunks, long Length)>();

        await using var payloadStream = new MemoryStream(payload, writable: false);
        await using var decompressionStream = new DecompressionStream(payloadStream);
        using var reader = new BinaryReader(decompressionStream, Encoding.UTF8, leaveOpen: true);

        var chunkChecksums = await ChecksumCompressor.TransposeDecompressHashesAsync(decompressionStream, cancellationToken);
        var batchCount = await VarIntUtils.ReadVarIntAsync<int>(decompressionStream);

        for (var i = 0; i < batchCount; i++)
        {
            var fileChecksumBytes = reader.ReadBytes(32);
            if (fileChecksumBytes.Length != 32)
                throw new InvalidOperationException("Invalid file checksum length in batch payload.");

            var fileChecksum = new Hash32(fileChecksumBytes);
            var fileLength = await VarIntUtils.ReadVarIntAsync<long>(decompressionStream);
            var chunkCount = await VarIntUtils.ReadVarIntAsync<int>(decompressionStream);

            var chunks = new List<Hash32>(chunkCount);

            for (var j = 0; j < chunkCount; j++)
            {
                var chunkIndex = await VarIntUtils.ReadVarIntAsync<int>(decompressionStream);
                if (chunkIndex < 0 || chunkIndex >= chunkChecksums.Count)
                    throw new InvalidOperationException("Invalid chunk index in batch payload.");

                chunks.Add(chunkChecksums[chunkIndex]);
            }

            result[fileChecksum] = (chunks, fileLength);
        }

        return result;
    }
}