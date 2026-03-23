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

using BinStash.Core.Chunking;
using BinStash.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;

namespace BinStash.Cli.Infrastructure;

public sealed class BinStashGrpcClient
{
    private readonly IngestService.IngestServiceClient _chunkIngest;

    public BinStashGrpcClient(string rootUrl)
    {
        var channel = GrpcChannel.ForAddress(rootUrl);
        _chunkIngest = new IngestService.IngestServiceClient(channel);
    }

    public async Task UploadChunksAsync(Guid repoId, Guid ingestSessionId, IChunker chunker, IEnumerable<ChunkMapEntry> chunksToUpload, Func<int, int, Task>? progressCallback = null, Func<UploadChunksReply, Task>? completedCallback = null, CancellationToken cancellationToken = default)
    {
        var chunkList = chunksToUpload as IList<ChunkMapEntry> ?? chunksToUpload.ToList();
        var total = chunkList.Count;
        var uploaded = 0;

        var headers = new Metadata
        {
            { "x-ingest-session-id", ingestSessionId.ToString() }
        };

        using var call = _chunkIngest.UploadChunks(headers: headers, cancellationToken: cancellationToken);

        foreach (var chunk in chunkList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var data = (await chunker.LoadChunkDataAsync(chunk, cancellationToken)).Data;

            await call.RequestStream.WriteAsync(new UploadChunkRequest
            {
                RepoId = repoId.ToString(),
                Checksum = ByteString.CopyFrom(chunk.Checksum.GetBytes()),
                Data = ByteString.CopyFrom(data)
            }, cancellationToken);

            uploaded++;

            if (progressCallback != null)
                await progressCallback(uploaded, total);
        }

        await call.RequestStream.CompleteAsync();

        var reply = await call.ResponseAsync.ConfigureAwait(false);

        /*_console?.WriteLine(
            $"gRPC upload complete: seen_total={reply.ChunksSeenTotal}, " +
            $"seen_unique={reply.ChunksSeenUnique}, " +
            $"written_new={reply.ChunksWrittenNew}, " +
            $"logical_bytes={reply.NewUniqueLogicalBytes}, " +
            $"compressed_bytes={reply.NewCompressedBytes}");*/

        if (completedCallback != null)
            await completedCallback(reply);
    }
}