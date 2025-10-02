// Copyright (C) 2025  Lukas Eßmann
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

using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using ZstdNet;

namespace BinStash.Infrastructure.Storage.Packing;

internal static class PackFileEntry
{
    private const int HeaderSize = 21;
    private const uint Magic = 0x4B505342; // ASCII "BSPK" => "BinStash PackFile" / Little-endian encoded
    private const byte Version = 1;
    
    public static async Task<(long, int)> WriteAsync(Stream output, byte[] data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        
        var offset = output.Position;
        
        var compressedData = CompressData(data);
        var compressedDataChecksum = XxHash3.HashToUInt64(compressedData);
        var uncompressedLength = data.Length;
        var compressedLength = compressedData.Length;
        
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], Magic);              // 0–3
        header[4] = Version;                                                       // 4
        BinaryPrimitives.WriteUInt32LittleEndian(header[5..9], (uint)uncompressedLength); // 5–8
        BinaryPrimitives.WriteUInt32LittleEndian(header[9..13], (uint)compressedLength);  // 9–12
        BinaryPrimitives.WriteUInt64LittleEndian(header[13..21], compressedDataChecksum); // 13–20

        await output.WriteAsync(header.ToArray(), ct);
        await output.WriteAsync(compressedData.AsMemory(0, compressedLength), ct);
        await output.FlushAsync(ct);
        
        return (offset, (int)(output.Position - offset));
    }
    
    public static async Task<byte[]?> ReadAsync(Stream input, bool ignoreChecks = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var headerBuf = new byte[HeaderSize];
        var read = await input.ReadAsync(headerBuf.AsMemory(0, HeaderSize), ct);
        if (read == 0) return null;
        if (read != HeaderSize) throw new InvalidDataException("Incomplete header");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(0, 4));
        if (!ignoreChecks && magic != Magic) throw new InvalidDataException("Bad magic");

        var version = headerBuf[4];
        if (!ignoreChecks && version != Version) throw new NotSupportedException($"Unsupported version {version}");

        var uncompressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(5, 4));
        var compressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(9, 4));
        var expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(headerBuf.AsSpan(13, 8));

        var compressed = new byte[compressedLength];
        var totalRead = 0;
        while (totalRead < compressedLength)
        {
            var r = await input.ReadAsync(compressed.AsMemory(totalRead, compressedLength - totalRead), ct);
            if (r == 0) throw new EndOfStreamException("Unexpected EOF in pack file entry");
            totalRead += r;
        }
        
        var actualChecksum = XxHash3.HashToUInt64(compressed);
        if (actualChecksum != expectedChecksum)
            throw new InvalidDataException("Checksum mismatch – data corrupted");

        var decompressed = DecompressData(compressed);
        if (decompressed.Length != uncompressedLength)
            throw new InvalidDataException("Decompressed length mismatch – data corrupted");
        
        return decompressed;
    }
    
    public static async IAsyncEnumerable<(long Offset, int Length, byte[] Data)> ReadAllEntriesAsync(Stream input, bool ignoreChecks = false, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        
        while (true)
        {
            var offset = input.Position;
            var entry = await ReadAsync(input, ignoreChecks, ct);
            if (entry == null) yield break;
            var length = (int)(input.Position - offset);
            yield return (offset, length, entry);
        }
    }
    
    private static byte[] CompressData(byte[] data)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(data);
    }

    private static byte[] DecompressData(byte[] compressedData)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(compressedData);
    }
}