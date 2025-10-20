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
using System.Text;
using BinStash.Contracts.Release;
using BinStash.Core.Serialization.Utils;
using Microsoft.IO;
using ZstdNet;

namespace BinStash.Core.Serialization;

public abstract class ReleasePackageSerializerBase
{
    
    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManager =
        new(new RecyclableMemoryStreamManager.Options(blockSize: 128 * 1024, largeBufferMultiple: 1024 * 1024, maximumBufferSize: 16 * 1024 * 1024, maximumSmallPoolFreeBytes: 256L * 1024 * 1024, maximumLargePoolFreeBytes: 512L * 1024 * 1024));
    
    protected static async Task WriteSectionAsync(Stream baseStream, byte id, Action<BinaryWriter> write, bool enableCompression, int compressionLevel, CancellationToken ct)
    {
        await using var ms = RecyclableMemoryStreamManager.GetStream();
        var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        write(w);
        w.Flush();

        ms.Position = 0;
        
        if (enableCompression)
        {
            await using var compressed = RecyclableMemoryStreamManager.GetStream();
            var compOptions = new CompressionOptions(compressionLevel);
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
            // TODO: validate the header is consistent with an empty payload.
            // TODO: maybe throw when bits>0 or packedLength>0.
            if (packedLength > 0)
            {
                // Consume and discard to keep stream position correct.
                r.BaseStream.Seek(packedLength, SeekOrigin.Current);
            }
            return new List<DeltaChunkRef>(0);
        }
        
        // Validate packed length is plausible (avoid under-reads)
        // totalBits = count * (bitsDelta + bitsOffset + bitsLength)
        var totalBits = chunkCount * (ulong)(bitsDelta + bitsOffset + bitsLength);
        var expectedMinBytes = (uint)((totalBits + 7) / 8);
        
        if (packedLength < expectedMinBytes)
            throw new InvalidDataException($"Packed data too short: {packedLength} < expected {expectedMinBytes}.");
        
        if (packedLength > int.MaxValue)
            throw new InvalidDataException("Packed data too large to buffer.");
        
        // Read packed payload (ArrayPool to minimize GC for large blocks)
        var buf = ArrayPool<byte>.Shared.Rent((int)packedLength);
        try
        {
            var read = r.Read(buf, 0, (int)packedLength);
            if (read != packedLength)
                throw new EndOfStreamException(
                    $"Unexpected EOF while reading packed chunk refs. Read {read} of {packedLength} bytes.");

            var bitReader = new BitReader(new ReadOnlySpan<byte>(buf, 0, (int)packedLength));

            var chunks = new List<DeltaChunkRef>((int)chunkCount);
            for (var i = 0; i < chunkCount; i++)
            {
                // If any bit width is zero, don’t call ReadBits(0) unless your BitReader explicitly supports it.
                var delta  = bitsDelta  == 0 ? 0u : (uint)bitReader.ReadBits(bitsDelta);
                var offset = bitsOffset == 0 ? 0u : bitReader.ReadBits(bitsOffset);
                var length = bitsLength == 0 ? 0u : bitReader.ReadBits(bitsLength);

                chunks.Add(new DeltaChunkRef(delta, offset, length));
            }

            return chunks;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}