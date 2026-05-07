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

namespace BinStash.Cli.Utils;

internal sealed class StreamingFileIngest : IAsyncDisposable
{
    private readonly FileStream _output;
    private Blake3.Hasher _fileHasher;
    private readonly IStreamingChunker _chunker;
    private long _length;

    public StreamingFileIngest(string stagingPath, IStreamingChunker chunker)
    {
        _output = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true);
        _fileHasher = Blake3.Hasher.New();
        _chunker = chunker;
    }

    public async ValueTask AppendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _output.WriteAsync(buffer, cancellationToken);
        _fileHasher.Update(buffer.Span);
        _chunker.Append(buffer.Span);
        _length += buffer.Length;
    }

    public async Task<StreamingFileIngestResult> CompleteAsync(CancellationToken cancellationToken = default)
    {
        await _output.FlushAsync(cancellationToken);
        await _output.DisposeAsync();

        var fileHash = new Hash32(_fileHasher.Finalize().AsSpan());
        var chunkBoundaries = _chunker.Complete();

        var chunkMap = chunkBoundaries
            .Select(x => new ChunkBoundary
            {
                Offset = x.Offset,
                Length = x.Length,
                Checksum = x.Checksum
            })
            .ToList();

        return new StreamingFileIngestResult(fileHash, _length, chunkMap);
    }

    public async ValueTask DisposeAsync()
    {
        await _output.DisposeAsync();
        _chunker.Dispose();
    }
}

internal sealed record StreamingFileIngestResult(Hash32 FileHash, long FileSize, List<ChunkBoundary> ChunkMap);