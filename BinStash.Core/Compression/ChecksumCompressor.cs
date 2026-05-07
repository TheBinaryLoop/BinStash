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

using BinStash.Contracts.Hashing;
using BinStash.Core.Serialization.Utils;
using ZstdNet;

namespace BinStash.Core.Compression;

public static class ChecksumCompressor
{
    public static byte[] TransposeCompress(List<byte[]> hashes)
    {
        if (hashes.Count == 0)
            return "\0\0"u8.ToArray(); // varint 0

        const int hashSize = 32;
        var count = hashes.Count;
        var columns = new byte[hashSize][];

        for (var i = 0; i < hashSize; i++)
            columns[i] = new byte[count];

        for (var row = 0; row < count; row++)
        {
            var hash = hashes[row];
            if (hash.Length != hashSize)
                throw new InvalidDataException($"Expected {hashSize}-byte hash, got {hash.Length}.");

            for (var col = 0; col < hashSize; col++)
                columns[col][row] = hash[col];
        }

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        VarIntUtils.WriteVarInt(writer, count);

        using var zstd = new Compressor(new CompressionOptions(9));

        for (var i = 0; i < hashSize; i++)
        {
            var compressed = zstd.Wrap(columns[i]);
            VarIntUtils.WriteVarInt(writer, compressed.Length);
            writer.Write(compressed);
        }

        return ms.ToArray();
    }

    public static async Task<List<Hash32>> TransposeDecompressHashesAsync(Stream inputStream, CancellationToken ct = default)
    {
        var count = await VarIntUtils.ReadVarIntAsync<int>(inputStream).ConfigureAwait(false);

        if (count == 0)
            return new List<Hash32>(0);

        const int hashSize = 32;
        var columns = new byte[hashSize][];
        using var zstd = new Decompressor();

        for (var i = 0; i < hashSize; i++)
        {
            var len = await VarIntUtils.ReadVarIntAsync<int>(inputStream).ConfigureAwait(false);
            if (len < 0)
                throw new InvalidDataException("Negative column length.");

            var compressed = GC.AllocateUninitializedArray<byte>(len);
            await ReadExactlyAsync(inputStream, compressed, ct).ConfigureAwait(false);

            columns[i] = zstd.Unwrap(compressed, count);
        }

        var result = new List<Hash32>(count);
        Span<byte> hashBuffer = stackalloc byte[hashSize];

        for (var row = 0; row < count; row++)
        {
            for (var col = 0; col < hashSize; col++)
                hashBuffer[col] = columns[col][row];

            result.Add(new Hash32(hashBuffer));
        }

        return result;
    }

    public static List<Hash32> TransposeDecompressHashes(Stream inputStream)
    {
        using var reader = new BinaryReader(inputStream);
        using var zstd = new Decompressor();

        var count = VarIntUtils.ReadVarInt<int>(reader);

        if (count == 0)
            return new List<Hash32>(0);

        const int hashSize = 32;
        var columns = new byte[hashSize][];

        for (var i = 0; i < hashSize; i++)
        {
            var len = VarIntUtils.ReadVarInt<int>(reader);
            if (len < 0)
                throw new InvalidDataException("Negative column length.");

            var compressed = reader.ReadBytes(len);
            if (compressed.Length != len)
                throw new EndOfStreamException();

            columns[i] = zstd.Unwrap(compressed, count);
        }

        var result = new List<Hash32>(count);
        Span<byte> hashBuffer = stackalloc byte[hashSize];

        for (var row = 0; row < count; row++)
        {
            for (var col = 0; col < hashSize; col++)
                hashBuffer[col] = columns[col][row];

            result.Add(new Hash32(hashBuffer));
        }

        return result;
    }

    public static List<Hash32> TransposeDecompressHashes(byte[] input)
    {
        using var ms = new MemoryStream(input, writable: false);
        return TransposeDecompressHashes(ms);
    }

    // Backward-compatible wrappers; remove usage over time
    
    public static List<byte[]> TransposeDecompress(Stream inputStream)
    {
        var hashes = TransposeDecompressHashes(inputStream);
        var result = new List<byte[]>(hashes.Count);
        foreach (var hash in hashes)
            result.Add(hash.GetBytes());
        return result;
    }
    
    // -------- helpers (async, no sync IO) --------
    // Reads exactly 'dest.Length' bytes or throws EndOfStreamException.
    private static async Task ReadExactlyAsync(Stream s, Memory<byte> dest, CancellationToken ct)
    {
        var readTotal = 0;
        while (readTotal < dest.Length)
        {
            var read = await s.ReadAsync(dest[readTotal..], ct).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            readTotal += read;
        }
    }
}