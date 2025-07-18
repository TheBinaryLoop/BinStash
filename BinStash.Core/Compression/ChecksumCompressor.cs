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
        if (hashes.Count == 0) return [];

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

    public static List<byte[]> TransposeDecompress(Stream inputStream)
    {
        using var reader = new BinaryReader(inputStream);
        using var zstd = new Decompressor();

        var count = VarIntUtils.ReadVarInt<int>(reader);
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
}