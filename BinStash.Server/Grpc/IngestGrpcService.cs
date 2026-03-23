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

using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Services.ChunkStores;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Grpc;

public sealed class IngestGrpcService : BinStash.Grpc.IngestService.IngestServiceBase
{
    private readonly BinStashDbContext _db;
    private readonly IChunkStoreService _chunkStoreService;

    public IngestGrpcService(BinStashDbContext db, IChunkStoreService chunkStoreService)
    {
        _db = db;
        _chunkStoreService = chunkStoreService;
    }

    public override async Task<BinStash.Grpc.UploadChunksReply> UploadChunks(IAsyncStreamReader<BinStash.Grpc.UploadChunkRequest> requestStream, ServerCallContext context)
    {
        var ingestHeader = context.RequestHeaders.FirstOrDefault(x => x.Key == "x-ingest-session-id")?.Value;
        if (!Guid.TryParse(ingestHeader, out var ingestId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing or invalid x-ingest-session-id metadata."));

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

        Guid? repoId = null;
        ChunkStore? store = null;

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

            if (!Guid.TryParse(msg.RepoId, out var parsedRepoId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid repo id in stream item."));

            if (repoId == null)
            {
                repoId = parsedRepoId;
                var repo = await _db.Repositories.FindAsync(repoId.Value);
                if (repo == null)
                    throw new RpcException(new Status(StatusCode.NotFound, "Repository not found."));

                store = await _db.ChunkStores.FindAsync(repo.ChunkStoreId);
                if (store == null)
                    throw new RpcException(new Status(StatusCode.NotFound, "Chunk store not found."));
            }
            else if (repoId.Value != parsedRepoId)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "All streamed chunks must target the same repository."));
            }

            var hash = new Hash32(msg.Checksum.Span);

            if (!seenInRequest.Add(hash))
                continue;

            chunksSeenUnique++;

            var alreadyKnown = await _db.Chunks
                .AnyAsync(c => c.ChunkStoreId == store!.Id && c.Checksum == hash, context.CancellationToken);

            if (alreadyKnown)
                continue;

            var data = msg.Data.Memory.ToArray();
            var actualHash = new Hash32(Blake3.Hasher.Hash(data).AsSpan());
            if (actualHash != hash)
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Checksum mismatch for {msg.Checksum}."));

            var (success, bytesWritten) = await _chunkStoreService.StoreChunkAsync(store!, actualHash.ToHexString(), data);
            if (!success)
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to store chunk {msg.Checksum}."));

            if (bytesWritten > 0)
            {
                newlyWrittenCompressedBytes[hash] = bytesWritten;
                newlyWrittenLogicalBytes[hash] = data.Length;

                chunksToAdd.Add(new Chunk
                {
                    Checksum = hash,
                    ChunkStoreId = store!.Id,
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
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException pg &&
            pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
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

        return new BinStash.Grpc.UploadChunksReply
        {
            ChunksSeenTotal = chunksSeenTotal,
            ChunksSeenUnique = chunksSeenUnique,
            ChunksWrittenNew = chunksToAdd.Count,
            NewUniqueLogicalBytes = newlyWrittenLogicalBytes.Values.Sum(),
            NewCompressedBytes = newlyWrittenCompressedBytes.Values.Sum()
        };
    }
}