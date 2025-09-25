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

using System.IO.MemoryMappedFiles;

namespace BinStash.Core.Serialization.Utils;

public class VarIntUtils
{
    #region Write Methods
    
    #region BinaryWriter Overloads
    
    public static void WriteVarInt<T>(BinaryWriter w, T value) where T : struct
    {
        switch (value)
        {
            case int i:
                WriteSignedVarInt(w, i);
                break;

            case long l:
                WriteSignedVarInt(w, l);
                break;

            case uint ui:
                WriteUnsignedVarInt(w, ui);
                break;

            case ulong ul:
                WriteUnsignedVarInt(w, ul);
                break;

            case ushort us:
                WriteUnsignedVarInt(w, us);
                break;

            default:
                throw new NotSupportedException($"Type {typeof(T)} is not supported for varint serialization.");
        }
    }
    
    private static void WriteSignedVarInt(BinaryWriter w, long value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var len = EncodeSignedVarInt(value, buffer);
        w.Write(buffer[..len]);
    }

    private static void WriteUnsignedVarInt(BinaryWriter w, ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var len = EncodeUnsignedVarInt(value, buffer);
        w.Write(buffer[..len]);
    }

    private static void WriteUnsignedVarInt(BinaryWriter w, uint value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var len = EncodeUnsignedVarInt(value, buffer);
        w.Write(buffer[..len]);
    }
    
    #endregion
    
    #region Stream Overloads
    
    public static void WriteVarInt<T>(Stream output, T value) where T : struct
    {
        switch (value)
        {
            case int i:
                WriteSignedVarInt(output, i);
                break;

            case long l:
                WriteSignedVarInt(output, l);
                break;

            case uint ui:
                WriteUnsignedVarInt(output, ui);
                break;

            case ulong ul:
                WriteUnsignedVarInt(output, ul);
                break;

            case ushort us:
                WriteUnsignedVarInt(output, us);
                break;

            default:
                throw new NotSupportedException($"Type {typeof(T)} is not supported for varint serialization.");
        }
    }
    
    private static void WriteSignedVarInt(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var len = EncodeSignedVarInt(value, buffer);
        stream.Write(buffer[..len]);
    }

    private static void WriteUnsignedVarInt(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var len = EncodeUnsignedVarInt(value, buffer);
        stream.Write(buffer[..len]);
    }

    private static void WriteUnsignedVarInt(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[5];
        var len = EncodeUnsignedVarInt(value, buffer);
        stream.Write(buffer[..len]);
    }
    
    #endregion
    
    #endregion
    
    #region Read Methods

    #region BinaryReader Overloads

    public static T ReadVarInt<T>(BinaryReader r) where T : struct
    {
        var result = typeof(T) switch
        {
            { } t when t == typeof(int) => ReadSignedVarInt32(r),
            { } t when t == typeof(long) => ReadSignedVarInt64(r),
            { } t when t == typeof(uint) => ReadUnsignedVarInt32(r),
            { } t when t == typeof(ulong) => ReadUnsignedVarInt64(r),
            { } t when t == typeof(ushort) => (object)(ushort)ReadUnsignedVarInt32(r),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint deserialization.")
        };
        return (T)result;
    }
    
    private static uint ReadUnsignedVarInt32(BinaryReader r)
    {
        uint result = 0;
        var shift = 0;

        while (true)
        {
            var b = r.ReadByte();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 35)
                throw new FormatException("VarInt32 too long.");
        }

        return result;
    }

    private static ulong ReadUnsignedVarInt64(BinaryReader r)
    {
        ulong result = 0;
        var shift = 0;

        while (true)
        {
            var b = r.ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 70)
                throw new FormatException("VarInt64 too long.");
        }

        return result;
    }

    private static int ReadSignedVarInt32(BinaryReader r)
    {
        var raw = ReadUnsignedVarInt32(r);
        return (int)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }

    private static long ReadSignedVarInt64(BinaryReader r)
    {
        var raw = ReadUnsignedVarInt64(r);
        return (long)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }

    #endregion
    
    #region MemoryMappedViewAccessor Overloads
    
    public static T ReadVarInt<T>(MemoryMappedViewAccessor accessor, ref long pos) where T : struct
    {
        var result = typeof(T) switch
        {
            { } t when t == typeof(int) => ReadSignedVarInt32(accessor, ref pos),
            { } t when t == typeof(long) => ReadSignedVarInt64(accessor, ref pos),
            { } t when t == typeof(uint) => ReadUnsignedVarInt32(accessor, ref pos),
            { } t when t == typeof(ulong) => ReadUnsignedVarInt64(accessor, ref pos),
            { } t when t == typeof(ushort) => (object)(ushort)ReadUnsignedVarInt32(accessor, ref pos),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint deserialization.")
        };
        return (T)result;
    }
    
    private static uint ReadUnsignedVarInt32(MemoryMappedViewAccessor accessor, ref long pos)
    {
        uint result = 0;
        var shift = 0;

        while (true)
        {
            var b = accessor.ReadByte(pos++);
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 35)
                throw new FormatException("VarInt32 too long.");
        }

        return result;
    }

    private static ulong ReadUnsignedVarInt64(MemoryMappedViewAccessor accessor, ref long pos)
    {
        ulong result = 0;
        var shift = 0;

        while (true)
        {
            var b = accessor.ReadByte(pos++);
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 70)
                throw new FormatException("VarInt64 too long.");
        }

        return result;
    }

    private static int ReadSignedVarInt32(MemoryMappedViewAccessor accessor, ref long pos)
    {
        var raw = ReadUnsignedVarInt32(accessor, ref pos);
        return (int)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }

    private static long ReadSignedVarInt64(MemoryMappedViewAccessor accessor, ref long pos)
    {
        var raw = ReadUnsignedVarInt64(accessor, ref pos);
        return (long)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }
    
    #endregion
    
    #region Stream Overloads
    
    public static T ReadVarInt<T>(Stream s) where T : struct
    {
        var result = typeof(T) switch
        {
            { } t when t == typeof(int) => ReadSignedVarInt32(s),
            { } t when t == typeof(long) => ReadSignedVarInt64(s),
            { } t when t == typeof(uint) => ReadUnsignedVarInt32(s),
            { } t when t == typeof(ulong) => ReadUnsignedVarInt64(s),
            { } t when t == typeof(ushort) => (object)(ushort)ReadUnsignedVarInt32(s),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint deserialization.")
        };
        return (T)result;
    }
    
    private static uint ReadUnsignedVarInt32(Stream s)
    {
        uint result = 0;
        var shift = 0;

        Span<byte> b = stackalloc byte[1];
        while (true)
        {
            s.ReadExactly(b);
            result |= (uint)(b[0] & 0x7F) << shift;
            if ((b[0] & 0x80) == 0) break;
            shift += 7;
            if (shift > 35)
                throw new FormatException("VarInt32 too long.");
        }

        return result;
    }

    private static ulong ReadUnsignedVarInt64(Stream s)
    {
        ulong result = 0;
        var shift = 0;

        Span<byte> b = stackalloc byte[1];
        while (true)
        {
            s.ReadExactly(b);
            result |= (ulong)(b[0] & 0x7F) << shift;
            if ((b[0] & 0x80) == 0) break;
            shift += 7;
            if (shift > 70)
                throw new FormatException("VarInt64 too long.");
        }

        return result;
    }

    private static int ReadSignedVarInt32(Stream s)
    {
        var raw = ReadUnsignedVarInt32(s);
        return (int)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }

    private static long ReadSignedVarInt64(Stream s)
    {
        var raw = ReadUnsignedVarInt64(s);
        return (long)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }
    
    #endregion
    
    #endregion
    
    #region Helper Methods
    
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

    
    #endregion
    
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
}