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

namespace BinStash.Core.Serialization.Utils;

internal class VarIntUtils
{
    internal static void WriteVarInt<T>(BinaryWriter w, T value) where T : struct
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
        var zigZag = (ulong)((value << 1) ^ (value >> 63)); // ZigZag encoding
        WriteUnsignedVarInt(w, zigZag);
    }

    private static void WriteUnsignedVarInt(BinaryWriter w, ulong value)
    {
        while ((value & ~0x7FUL) != 0)
        {
            w.Write((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        w.Write((byte)value);
    }

    private static void WriteUnsignedVarInt(BinaryWriter w, uint value)
    {
        while ((value & ~0x7FU) != 0)
        {
            w.Write((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        w.Write((byte)value);
    }
    
    internal static T ReadVarInt<T>(BinaryReader r) where T : struct
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