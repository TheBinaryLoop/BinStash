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

using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using ZstdNet;

namespace BinStash.Infrastructure.Storage.Packing;

internal static class PackFileEntry
{
    private const int HeaderSize = 21;
    private const uint Magic = 0x4B505342; // "BSPK" little-endian
    private const byte Version = 1;

    private static readonly ThreadLocal<Compressor> CompressorPool = new(() => new Compressor());
    private static readonly ThreadLocal<Decompressor> DecompressorPool = new(() => new Decompressor());

    public static async Task<(long, int)> WriteAsync(Stream output, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var offset = output.Position;

        var compressedData = CompressData(data);
        var compressedDataChecksum = XxHash3.HashToUInt64(compressedData);
        var uncompressedLength = data.Length;
        var compressedLength = compressedData.Length;

        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], Magic);
        header[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(header[5..9], (uint)uncompressedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header[9..13], (uint)compressedLength);
        BinaryPrimitives.WriteUInt64LittleEndian(header[13..21], compressedDataChecksum);

        await output.WriteAsync(header.ToArray(), ct).ConfigureAwait(false);
        await output.WriteAsync(compressedData.AsMemory(0, compressedLength), ct).ConfigureAwait(false);

        // Kept per your request. Note: this is not the strongest durability boundary by itself.
        await output.FlushAsync(ct).ConfigureAwait(false);

        return (offset, HeaderSize + compressedLength);
    }

    public static async Task<byte[]?> ReadAsync(Stream input, bool ignoreChecks = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        byte[] headerBuf = GC.AllocateUninitializedArray<byte>(HeaderSize);

        var read = await input.ReadAsync(headerBuf.AsMemory(0, HeaderSize), ct).ConfigureAwait(false);
        if (read == 0)
            return null;

        if (read != HeaderSize)
            throw new InvalidDataException("Incomplete header");

        var header = headerBuf.AsSpan();

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        if (!ignoreChecks && magic != Magic)
            throw new InvalidDataException("Bad magic");

        var version = header[4];
        if (!ignoreChecks && version != Version)
            throw new NotSupportedException($"Unsupported version {version}");

        var uncompressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[5..9]);
        var compressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[9..13]);
        var expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(header[13..21]);

        var compressed = GC.AllocateUninitializedArray<byte>(compressedLength);

        var totalRead = 0;
        while (totalRead < compressedLength)
        {
            var r = await input.ReadAsync(compressed.AsMemory(totalRead, compressedLength - totalRead), ct).ConfigureAwait(false);
            if (r == 0)
                throw new EndOfStreamException("Unexpected EOF in pack file entry");

            totalRead += r;
        }

        if (!ignoreChecks)
        {
            var actualChecksum = XxHash3.HashToUInt64(compressed);
            if (actualChecksum != expectedChecksum)
                throw new InvalidDataException("Checksum mismatch – data corrupted");
        }

        var decompressed = DecompressData(compressed, uncompressedLength);

        if (!ignoreChecks && decompressed.Length != uncompressedLength)
            throw new InvalidDataException("Decompressed length mismatch – data corrupted");

        return decompressed;
    }

    public static async Task<byte[]?> ReadAtAsync(SafeFileHandle handle, long offset, bool ignoreChecks = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        byte[] headerBuf = GC.AllocateUninitializedArray<byte>(HeaderSize);

        var headerRead = await ReadExactlyAtOrEofAsync(handle, headerBuf.AsMemory(0, HeaderSize), offset, ct).ConfigureAwait(false);
        if (headerRead == 0)
            return null;

        if (headerRead != HeaderSize)
            throw new InvalidDataException("Incomplete header");

        var header = headerBuf.AsSpan();

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        if (!ignoreChecks && magic != Magic)
            throw new InvalidDataException("Bad magic");

        var version = header[4];
        if (!ignoreChecks && version != Version)
            throw new NotSupportedException($"Unsupported version {version}");

        var uncompressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[5..9]);
        var compressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[9..13]);
        var expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(header[13..21]);

        var compressed = GC.AllocateUninitializedArray<byte>(compressedLength);

        var bodyRead = await ReadExactlyAtOrEofAsync(handle, compressed.AsMemory(0, compressedLength), offset + HeaderSize, ct).ConfigureAwait(false);
        if (bodyRead != compressedLength)
            throw new EndOfStreamException("Unexpected EOF in pack file entry");

        if (!ignoreChecks)
        {
            var actualChecksum = XxHash3.HashToUInt64(compressed);
            if (actualChecksum != expectedChecksum)
                throw new InvalidDataException("Checksum mismatch – data corrupted");
        }

        var decompressed = DecompressData(compressed, uncompressedLength);

        if (!ignoreChecks && decompressed.Length != uncompressedLength)
            throw new InvalidDataException("Decompressed length mismatch – data corrupted");

        return decompressed;
    }

    public static int ReadUncompressedLength(SafeFileHandle handle, long offset)
    {
        Span<byte> buf = stackalloc byte[9];

        var totalRead = 0;
        while (totalRead < buf.Length)
        {
            var bytesRead = RandomAccess.Read(handle, buf[totalRead..], offset + totalRead);
            if (bytesRead == 0)
                throw new EndOfStreamException("Unexpected EOF while reading pack header.");

            totalRead += bytesRead;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(buf[..4]);
        if (magic != Magic)
            throw new InvalidDataException("Bad magic");

        var version = buf[4];
        if (version != Version)
            throw new NotSupportedException($"Unsupported version {version}");

        return (int)BinaryPrimitives.ReadUInt32LittleEndian(buf[5..9]);
    }

    public static async IAsyncEnumerable<(long Offset, int Length, byte[] Data)> ReadAllEntriesAsync(Stream input, bool ignoreChecks = false, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        while (true)
        {
            var offset = input.Position;

            var entry = await ReadAsync(input, ignoreChecks, ct).ConfigureAwait(false);
            if (entry == null)
                yield break;

            var length = (int)(input.Position - offset);
            yield return (offset, length, entry);
        }
    }

    private static byte[] CompressData(ReadOnlyMemory<byte> data)
    {
        return CompressorPool.Value!.Wrap(data.Span);
    }

    private static byte[] DecompressData(byte[] compressedData, int expectedUncompressedLength)
    {
        return DecompressorPool.Value!.Unwrap(compressedData, expectedUncompressedLength);
    }

    private static async Task<int> ReadExactlyAtOrEofAsync(SafeFileHandle handle, Memory<byte> buffer, long offset, CancellationToken ct)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await RandomAccess.ReadAsync(handle, buffer[totalRead..], offset + totalRead, ct).ConfigureAwait(false);

            if (read == 0)
                return totalRead;

            totalRead += read;
        }

        return totalRead;
    }
}