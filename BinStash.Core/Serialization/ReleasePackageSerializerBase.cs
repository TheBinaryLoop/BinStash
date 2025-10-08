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

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using BinStash.Contracts.Release;
using BinStash.Core.Serialization.Utils;
using Microsoft.IO;
using ZstdNet;

namespace BinStash.Core.Serialization;

public abstract class ReleasePackageSerializerBase
{
    // Computes the number of bits needed to encode [0..max] (0 => 0 bits).
    private static byte Bits32(uint max) => max == 0 ? (byte)0 : (byte)(BitOperations.Log2(max) + 1);
    private static byte Bits64(ulong max) => max == 0 ? (byte)0 : (byte)(BitOperations.Log2(max) + 1);
    
    protected static readonly Dictionary<byte, byte[]> ZstDicts = new()
    {
        //{ 0x03, File.ReadAllBytes(@"C:\Tmp\dict_sample\strings-64.dict") }
    };
    
    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManager =
        new(new RecyclableMemoryStreamManager.Options(blockSize: 128 * 1024, largeBufferMultiple: 1024 * 1024, maximumBufferSize: 16 * 1024 * 1024, maximumSmallPoolFreeBytes: 256L * 1024 * 1024, maximumLargePoolFreeBytes: 512L * 1024 * 1024));
    
    protected static async Task WriteSectionAsync(Stream baseStream, byte id, Action<BinaryWriter> write, bool enableCompression, int compressionLevel, CancellationToken ct)
    {
        var streamPosition = baseStream.Position;
        await using var ms = RecyclableMemoryStreamManager.GetStream();
        var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        write(w);
        w.Flush();

        ms.Position = 0;
        
        if (enableCompression)
        {
            await using var compressed = RecyclableMemoryStreamManager.GetStream();
            var compOptions = ZstDicts.TryGetValue(id, out var zstDict) ? new CompressionOptions(zstDict, compressionLevel) : new CompressionOptions(compressionLevel);
            await using (var z = new CompressionStream(compressed, compOptions))
                await ms.CopyToAsync(z, 1 << 20, ct);  

            compressed.Position = 0;
            var writer = new BinaryWriter(baseStream, Encoding.UTF8, true);
            writer.Write(id);
            writer.Write((byte)0); // FLAG: Currently unused, reserved for future use
            VarIntUtils.WriteVarInt(writer, (ulong)compressed.Length);
            await compressed.CopyToAsync(baseStream, ct);
        }
        else
        {
            // Write uncompressed section data
            var writer = new BinaryWriter(baseStream, Encoding.UTF8, true);
            writer.Write(id);
            writer.Write((byte)0); // FLAG: Currently unused, reserved for future use
            VarIntUtils.WriteVarInt(writer, (ulong)ms.Length);
            await ms.CopyToAsync(baseStream, ct);
        }

        //Console.WriteLine($"Section {id:X} (P: {streamPosition}, L: {baseStream.Position - streamPosition}{(enableCompression ? $", R: {ms.Length}" : "")})");
    }

    protected static List<DeltaChunkRef> ReadChunkRefs(BinaryReader r)
    {
        var chunkCount = VarIntUtils.ReadVarInt<uint>(r);
        
        var bitsDelta  = r.ReadByte();
        var bitsOffset = r.ReadByte();
        var bitsLength = r.ReadByte();
        
        var packedLength = VarIntUtils.ReadVarInt<uint>(r);
        
        // Fast path: nothing to read
        if (chunkCount == 0)
        {
            // It's permissible to carry 0 bits-* and 0 payload.
            // If payload > 0, consume and discard to keep position correct but flag as invalid format.
            if (packedLength > 0)
            {
                r.BaseStream.Seek(packedLength, SeekOrigin.Current);
                throw new InvalidDataException("ChunkCount=0 but packed payload length > 0.");
            }
            if (bitsDelta != 0 || bitsOffset != 0 || bitsLength != 0)
                throw new InvalidDataException("ChunkCount=0 but non-zero bit widths present.");
            return new List<DeltaChunkRef>(0);
        }
        
        // Validate that zero width implies all values are zero for that field (enforced later during read)
        // Validate packed length is at least the minimal needed:
        // totalBits = count * (bitsDelta + bitsOffset + bitsLength)
        checked
        {
            var perEntryBits = (int)bitsDelta + (int)bitsOffset + (int)bitsLength;
            var totalBits = (ulong)chunkCount * (ulong)perEntryBits;
            var minBytes = (uint)((totalBits + 7) >> 3);
            if (packedLength < minBytes)
                throw new InvalidDataException($"Packed data too short: {packedLength} < expected >= {minBytes}.");
        }

        if (packedLength == 0 && (bitsDelta != 0 || bitsOffset != 0 || bitsLength != 0))
            throw new InvalidDataException("Non-zero bit widths but zero payload length.");

        if (packedLength > int.MaxValue)
            throw new InvalidDataException("Packed data too large to buffer.");
        
        // Read payload
        byte[]? rented = null;
        Span<byte> span;
        if (packedLength <= 1024)
        {
            span = new byte[(int)packedLength];
            var read = r.Read(span);
            if (read != packedLength)
                throw new EndOfStreamException($"Unexpected EOF reading packed chunk refs. Read {read} of {packedLength} bytes.");
        }
        else
        {
            rented = ArrayPool<byte>.Shared.Rent((int)packedLength);
            var read = r.Read(rented, 0, (int)packedLength);
            if (read != packedLength)
            {
                ArrayPool<byte>.Shared.Return(rented);
                throw new EndOfStreamException($"Unexpected EOF reading packed chunk refs. Read {read} of {packedLength} bytes.");
            }
            span = new Span<byte>(rented, 0, (int)packedLength);
        }

        try
        {
            var br = new BitReader(span);

            var result = new List<DeltaChunkRef>((int)chunkCount);
            for (var i = 0; i < chunkCount; i++)
            {
                var  delta  = bitsDelta  == 0 ? 0u : (uint)br.ReadBits(bitsDelta);
                var offset = bitsOffset == 0 ? 0UL : br.ReadBits(bitsOffset);
                var length = bitsLength == 0 ? 0UL : br.ReadBits(bitsLength);

                result.Add(new DeltaChunkRef(delta, offset, length));
            }

            return result;
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }
    
    protected static void WriteTokenSequence(BinaryWriter w, List<(ushort id, Separator sep)> tokens)
    {
        VarIntUtils.WriteVarInt(w, (ushort)tokens.Count);
        foreach (var (id, sep) in tokens)
        {
            VarIntUtils.WriteVarInt(w, id);
            w.Write((byte)sep);
        }
    }
    
    protected static string ReadTokenizedString(BinaryReader r, List<string> table)
    {
        var count = VarIntUtils.ReadVarInt<ushort>(r);
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            var id = VarIntUtils.ReadVarInt<ushort>(r);
            var sep = (Separator)r.ReadByte();
            sb.Append(table[id]);
            if (sep != Separator.None)
                sb.Append((char)sep);
        }

        return sb.ToString();
    }
}