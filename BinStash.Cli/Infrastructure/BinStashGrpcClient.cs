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
using BinStash.Core.Chunking;
using BinStash.Core.Compression;
using BinStash.Core.Serialization.Utils;
using BinStash.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using ZstdNet;

namespace BinStash.Cli.Infrastructure;

public sealed class BinStashGrpcClient
{
    private readonly IngestService.IngestServiceClient _ingestClient;
    private readonly Func<Task<string>> _authTokenFactory;


    public BinStashGrpcClient(string rootUrl, Func<Task<string>> authTokenFactory)
    {
        _authTokenFactory = authTokenFactory;
        var channel = GrpcChannel.ForAddress(rootUrl);
        _ingestClient = new IngestService.IngestServiceClient(channel);
    }

    public async Task UploadFileDefinitionsAsync(Guid repoId, Guid ingestSessionId, Dictionary<Hash32, (List<Hash32> Chunks, long Length)> fileDefinitionsToUpload, int batchSize = 1000, Func<int, int, Task>? progressCallback = null, Func<UploadFileDefinitionsReply, Task>? completedCallback = null,  CancellationToken cancellationToken = default)
    {
        var total = fileDefinitionsToUpload.Count;
        var uploaded = 0;

        var headers = new Metadata
        {
            { "authorization", $"Bearer {await _authTokenFactory()}" },
            { "x-ingest-session-id", ingestSessionId.ToString() },
            { "x-repo-id", repoId.ToString() }
        };

        using var call = _ingestClient.UploadFileDefinitions(headers: headers, cancellationToken: cancellationToken);

        foreach (var batch in fileDefinitionsToUpload.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = await BuildFileDefinitionsBatchPayloadAsync(batch, cancellationToken);

            await call.RequestStream.WriteAsync(new UploadFileDefinitionsRequest
            {
                // bytes: May contain any arbitrary sequence of bytes no longer than 2^32.
                Payload = ByteString.CopyFrom(payload)
            }, cancellationToken);

            uploaded += batch.Length;

            if (progressCallback != null)
                await progressCallback(uploaded, total);
        }

        await call.RequestStream.CompleteAsync();

        var reply = await call.ResponseAsync.ConfigureAwait(false);

        if (completedCallback != null)
            await completedCallback(reply);
    }
    
    public async Task UploadChunksAsync(Guid repoId, Guid ingestSessionId, IChunker chunker, IEnumerable<ChunkMapEntry> chunksToUpload, Func<int, int, Task>? progressCallback = null, Func<UploadChunksReply, Task>? completedCallback = null, CancellationToken cancellationToken = default)
    {
        var chunkList = chunksToUpload as IList<ChunkMapEntry> ?? chunksToUpload.ToList();
        var total = chunkList.Count;
        var uploaded = 0;
        
        var headers = new Metadata
        {
            { "authorization", $"Bearer {await _authTokenFactory()}" },
            { "x-ingest-session-id", ingestSessionId.ToString() },
            { "x-repo-id", repoId.ToString() }
        };

        using var call = _ingestClient.UploadChunks(headers: headers, cancellationToken: cancellationToken);

        foreach (var chunk in chunkList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var data = (await chunker.LoadChunkDataAsync(chunk, cancellationToken)).Data;

            await call.RequestStream.WriteAsync(new UploadChunkRequest
            {
                Checksum = ByteString.CopyFrom(chunk.Checksum.GetBytes()),
                // bytes: May contain any arbitrary sequence of bytes no longer than 2^32.
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
    
    private static async Task<byte[]> BuildFileDefinitionsBatchPayloadAsync(KeyValuePair<Hash32, (List<Hash32> Chunks, long Length)>[] batch, CancellationToken cancellationToken)
    {
        var hashesForBatch = batch
            .SelectMany(x => x.Value.Chunks)
            .Distinct()
            .ToList();

        var indexMap = new Dictionary<Hash32, int>(hashesForBatch.Count);
        for (var idx = 0; idx < hashesForBatch.Count; idx++)
            indexMap[hashesForBatch[idx]] = idx;

        using var ms = new MemoryStream();

        await using (var compressionStream = new CompressionStream(ms))
        {
            var transposedChecksums = ChecksumCompressor.TransposeCompress(
                hashesForBatch.Select(x => x.GetBytes()).ToList());

            await compressionStream.WriteAsync(transposedChecksums, cancellationToken);

            await VarIntUtils.WriteVarIntAsync(compressionStream, batch.Length, cancellationToken);

            foreach (var (fileHash, fileDef) in batch)
            {
                await compressionStream.WriteAsync(fileHash.GetBytes(), cancellationToken);
                await VarIntUtils.WriteVarIntAsync(compressionStream, fileDef.Length, cancellationToken);
                await VarIntUtils.WriteVarIntAsync(compressionStream, fileDef.Chunks.Count, cancellationToken);

                foreach (var chunkHash in fileDef.Chunks)
                    await VarIntUtils.WriteVarIntAsync(compressionStream, indexMap[chunkHash], cancellationToken);
            }
        }

        return ms.ToArray();
    }
}