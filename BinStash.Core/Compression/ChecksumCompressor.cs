﻿// Copyright (C) 2025  Lukas Eßmann
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

using BinStash.Core.Serialization.Utils;
using ZstdNet;

namespace BinStash.Core.Compression;

public static class ChecksumCompressor
{
    public static byte[] TransposeCompress(List<byte[]> hashes)
    {
        if (hashes.Count == 0) return "\0\0"u8.ToArray(); // varint 0

        const int hashSize = 32;
        var count = hashes.Count;
        var columns = new byte[hashSize][];

        for (var i = 0; i < hashSize; i++)
            columns[i] = new byte[count];

        for (var row = 0; row < count; row++)
        {
            var hash = hashes[row];
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

    public static async Task<List<byte[]>> TransposeDecompressAsync(Stream inputStream, CancellationToken ct = default)
    {
        // count (signed varint)
        var count = await ReadSignedVarInt32Async(inputStream, ct).ConfigureAwait(false);
        
        if (count == 0) 
            return new List<byte[]>(0);

        const int hashSize = 32;

        var columns = new byte[hashSize][];
        using var zstd = new Decompressor();

        for (var i = 0; i < hashSize; i++)
        {
            // column length (signed varint in your current format)
            var len = await ReadSignedVarInt32Async(inputStream, ct).ConfigureAwait(false);
            if (len < 0) throw new InvalidDataException("Negative column length.");

            // read compressed column payload
            var compressed = GC.AllocateUninitializedArray<byte>(len);
            await ReadExactlyAsync(inputStream, compressed, ct).ConfigureAwait(false);

            // decompress to column-major buffer (length = count)
            columns[i] = zstd.Unwrap(compressed, count);
        }

        // transpose to row-major hashes
        var result = new List<byte[]>(count);
        for (var row = 0; row < count; row++)
        {
            var hash = new byte[hashSize];
            for (var col = 0; col < hashSize; col++)
                hash[col] = columns[col][row];
            result.Add(hash);
        }

        return result;
    }

    public static List<byte[]> TransposeDecompress(Stream inputStream)
    {
        using var reader = new BinaryReader(inputStream);
        using var zstd = new Decompressor();

        var count = VarIntUtils.ReadVarInt<int>(reader);
        
        if (count == 0) 
            return new List<byte[]>(0);
        
        const int hashSize = 32;

        var columns = new byte[hashSize][];
        for (var i = 0; i < hashSize; i++)
        {
            var len = VarIntUtils.ReadVarInt<int>(reader);
            var compressed = reader.ReadBytes(len);
            columns[i] = zstd.Unwrap(compressed, count);
        }

        var result = new List<byte[]>(count);
        for (var row = 0; row < count; row++)
        {
            var hash = new byte[hashSize];
            for (var col = 0; col < hashSize; col++)
                hash[col] = columns[col][row];
            result.Add(hash);
        }

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
    
    // VarInt (LEB128) unsigned 32-bit, async byte-by-byte.
    private static async Task<uint> ReadUnsignedVarInt32Async(Stream s, CancellationToken ct)
    {
        uint result = 0;
        var shift = 0;
        var one = new byte[1];

        while (true)
        {
            await ReadExactlyAsync(s, one, ct).ConfigureAwait(false);
            var b = one[0];

            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;

            shift += 7;
            if (shift > 35) throw new FormatException("VarInt32 too long.");
        }
        return result;
    }
    
    // ZigZag decode wrappers to match your VarIntUtils<int> usage.
    private static async Task<int> ReadSignedVarInt32Async(Stream s, CancellationToken ct)
    {
        var raw = await ReadUnsignedVarInt32Async(s, ct).ConfigureAwait(false);
        // ZigZag decode: (raw >> 1) ^ (~(raw & 1) + 1)
        return (int)((raw >> 1) ^ (~(raw & 1u) + 1u));
    }
}