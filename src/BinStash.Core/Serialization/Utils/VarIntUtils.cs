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

using System.Buffers;
using System.IO.MemoryMappedFiles;

namespace BinStash.Core.Serialization.Utils;

public static class VarIntUtils
{
    #region Write Methods

    public static void WriteVarInt<T>(BinaryWriter w, T value) where T : struct
        => WriteVarIntCore(value, bytes => w.Write(bytes));

    public static void WriteVarInt<T>(Stream stream, T value) where T : struct
        => WriteVarIntCore(value, bytes => stream.Write(bytes));

    public static void WriteVarInt<T>(IBufferWriter<byte> w, T value) where T : struct
        => WriteVarIntCore(value, bytes => w.Write(bytes));

    public static int WriteVarInt<T>(Span<byte> dest, T value) where T : struct
    {
        return value switch
        {
            int i => EncodeSignedVarInt(i, dest),
            long l => EncodeSignedVarInt(l, dest),
            uint ui => EncodeUnsignedVarInt(ui, dest),
            ulong ul => EncodeUnsignedVarInt(ul, dest),
            ushort us => EncodeUnsignedVarInt(us, dest),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint serialization.")
        };
    }

    public static async Task WriteVarIntAsync<T>(Stream stream, T value, CancellationToken ct = default) where T : struct
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(10);
        try
        {
            var span = rented.AsSpan();
            var len = value switch
            {
                int i => EncodeSignedVarInt(i, span),
                long l => EncodeSignedVarInt(l, span),
                uint ui => EncodeUnsignedVarInt(ui, span),
                ulong ul => EncodeUnsignedVarInt(ul, span),
                ushort us => EncodeUnsignedVarInt(us, span),
                _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint serialization.")
            };

            await stream.WriteAsync(rented.AsMemory(0, len), ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void WriteVarIntCore<T>(T value, Action<ReadOnlySpan<byte>> write) where T : struct
    {
        Span<byte> buffer = stackalloc byte[10];
        var len = value switch
        {
            int i => EncodeSignedVarInt(i, buffer),
            long l => EncodeSignedVarInt(l, buffer),
            uint ui => EncodeUnsignedVarInt(ui, buffer),
            ulong ul => EncodeUnsignedVarInt(ul, buffer),
            ushort us => EncodeUnsignedVarInt(us, buffer),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint serialization.")
        };

        write(buffer[..len]);
    }

    #endregion

    #region Read Methods

    public static T ReadVarInt<T>(BinaryReader r) where T : struct
    {
        object result = typeof(T) switch
        {
            { } t when t == typeof(int) => ReadSignedVarInt32Core(r.ReadByte),
            { } t when t == typeof(long) => ReadSignedVarInt64Core(r.ReadByte),
            { } t when t == typeof(uint) => ReadUnsignedVarInt32Core(r.ReadByte),
            { } t when t == typeof(ulong) => ReadUnsignedVarInt64Core(r.ReadByte),
            { } t when t == typeof(ushort) => checked((ushort)ReadUnsignedVarInt32Core(r.ReadByte)),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint deserialization.")
        };

        return (T)result;
    }

    public static T ReadVarInt<T>(MemoryMappedViewAccessor accessor, ref long pos) where T : struct
    {
        var localPos = pos;

        byte ReadByte()
        {
            var b = accessor.ReadByte(localPos);
            localPos++;
            return b;
        }

        object result = typeof(T) switch
        {
            { } t when t == typeof(int) => ReadSignedVarInt32Core(ReadByte),
            { } t when t == typeof(long) => ReadSignedVarInt64Core(ReadByte),
            { } t when t == typeof(uint) => ReadUnsignedVarInt32Core(ReadByte),
            { } t when t == typeof(ulong) => ReadUnsignedVarInt64Core(ReadByte),
            { } t when t == typeof(ushort) => checked((ushort)ReadUnsignedVarInt32Core(ReadByte)),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint deserialization.")
        };

        pos = localPos; // write back to ref var

        return (T)result;
    }

    public static async Task<T> ReadVarIntAsync<T>(Stream s) where T : struct
    {
        var buffer = new byte[1];

        async ValueTask<byte> ReadByteAsync()
        {
            await s.ReadExactlyAsync(buffer);
            return buffer[0];
        }

        object result = typeof(T) switch
        {
            { } t when t == typeof(int) => await ReadSignedVarInt32CoreAsync(ReadByteAsync),
            { } t when t == typeof(long) => await ReadSignedVarInt64CoreAsync(ReadByteAsync),
            { } t when t == typeof(uint) => await ReadUnsignedVarInt32CoreAsync(ReadByteAsync),
            { } t when t == typeof(ulong) => await ReadUnsignedVarInt64CoreAsync(ReadByteAsync),
            { } t when t == typeof(ushort) => checked((ushort)await ReadUnsignedVarInt32CoreAsync(ReadByteAsync)),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint deserialization.")
        };

        return (T)result;
    }

    #endregion

    #region Core Read Logic

    private static uint ReadUnsignedVarInt32Core(Func<byte> readByte)
    {
        uint result = 0;

        for (var i = 0; i < 5; i++)
        {
            var b = readByte();

            if (i == 4)
            {
                if ((b & 0xF0) != 0)
                    throw new FormatException("VarInt32 overflow.");

                result |= (uint)(b & 0x0F) << 28;

                if ((b & 0x80) != 0)
                    throw new FormatException("VarInt32 too long.");

                return result;
            }

            result |= (uint)(b & 0x7F) << (7 * i);

            if ((b & 0x80) == 0)
                return result;
        }

        throw new FormatException("VarInt32 too long.");
    }

    private static ulong ReadUnsignedVarInt64Core(Func<byte> readByte)
    {
        ulong result = 0;

        for (var i = 0; i < 10; i++)
        {
            var b = readByte();

            if (i == 9)
            {
                if ((b & 0xFE) != 0)
                    throw new FormatException("VarInt64 overflow.");

                result |= (ulong)(b & 0x01) << 63;

                if ((b & 0x80) != 0)
                    throw new FormatException("VarInt64 too long.");

                return result;
            }

            result |= (ulong)(b & 0x7F) << (7 * i);

            if ((b & 0x80) == 0)
                return result;
        }

        throw new FormatException("VarInt64 too long.");
    }

    private static int ReadSignedVarInt32Core(Func<byte> readByte)
    {
        var raw = ReadUnsignedVarInt32Core(readByte);
        return (int)((raw >> 1) ^ (uint)-(int)(raw & 1));
    }

    private static long ReadSignedVarInt64Core(Func<byte> readByte)
    {
        var raw = ReadUnsignedVarInt64Core(readByte);
        return (long)((raw >> 1) ^ (ulong)-(long)(raw & 1));
    }

    private static async Task<uint> ReadUnsignedVarInt32CoreAsync(Func<ValueTask<byte>> readByteAsync)
    {
        uint result = 0;

        for (var i = 0; i < 5; i++)
        {
            var b = await readByteAsync();

            if (i == 4)
            {
                if ((b & 0xF0) != 0)
                    throw new FormatException("VarInt32 overflow.");

                result |= (uint)(b & 0x0F) << 28;

                if ((b & 0x80) != 0)
                    throw new FormatException("VarInt32 too long.");

                return result;
            }

            result |= (uint)(b & 0x7F) << (7 * i);

            if ((b & 0x80) == 0)
                return result;
        }

        throw new FormatException("VarInt32 too long.");
    }

    private static async Task<ulong> ReadUnsignedVarInt64CoreAsync(Func<ValueTask<byte>> readByteAsync)
    {
        ulong result = 0;

        for (var i = 0; i < 10; i++)
        {
            var b = await readByteAsync();

            if (i == 9)
            {
                if ((b & 0xFE) != 0)
                    throw new FormatException("VarInt64 overflow.");

                result |= (ulong)(b & 0x01) << 63;

                if ((b & 0x80) != 0)
                    throw new FormatException("VarInt64 too long.");

                return result;
            }

            result |= (ulong)(b & 0x7F) << (7 * i);

            if ((b & 0x80) == 0)
                return result;
        }

        throw new FormatException("VarInt64 too long.");
    }

    private static async Task<int> ReadSignedVarInt32CoreAsync(Func<ValueTask<byte>> readByteAsync)
    {
        var raw = await ReadUnsignedVarInt32CoreAsync(readByteAsync);
        return (int)((raw >> 1) ^ (uint)-(int)(raw & 1));
    }

    private static async Task<long> ReadSignedVarInt64CoreAsync(Func<ValueTask<byte>> readByteAsync)
    {
        var raw = await ReadUnsignedVarInt64CoreAsync(readByteAsync);
        return (long)((raw >> 1) ^ (ulong)-(long)(raw & 1));
    }

    #endregion

    #region Encode Helpers

    private static int EncodeUnsignedVarInt(ulong value, Span<byte> buffer)
    {
        var index = 0;
        while ((value & ~0x7FUL) != 0)
        {
            buffer[index++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        buffer[index++] = (byte)value;
        return index;
    }

    private static int EncodeUnsignedVarInt(uint value, Span<byte> buffer)
    {
        var index = 0;
        while ((value & ~0x7FU) != 0)
        {
            buffer[index++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        buffer[index++] = (byte)value;
        return index;
    }

    private static int EncodeSignedVarInt(long value, Span<byte> buffer)
    {
        var zigZag = (ulong)((value << 1) ^ (value >> 63));
        return EncodeUnsignedVarInt(zigZag, buffer);
    }

    internal static int VarIntSize(ulong value)
    {
        var size = 0;
        do
        {
            size++;
            value >>= 7;
        } while (value != 0);

        return size;
    }

    #endregion
}