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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BinStash.Contracts.Hashing;

[JsonConverter(typeof(Hash32TypeConverter))]
public readonly struct Hash32 : IEquatable<Hash32>, IComparable<Hash32>
{
    private readonly ulong _h0, _h1, _h2, _h3; // pack 32 bytes into 4x ulong

    public Hash32(byte[] bytes) : this(bytes.AsSpan())
    {
    }

    
    public Hash32(ReadOnlySpan<byte> bytes)
    {
        /* pack 4*8 little-endian */
        if (bytes.Length != 32) throw new ArgumentException("Hash32 must be 32 bytes");
        _h0 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[0..8]);
        _h1 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..16]);
        _h2 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..24]);
        _h3 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..32]);
    }
    
    public static Hash32 FromHexString(string hex)
    {
        if (hex.Length != 64) throw new ArgumentException("Hash32 hex string must be 64 characters");
        Span<byte> bytes = stackalloc byte[32];
        Convert.FromHexString(hex, bytes, out _, out _);
        return new Hash32(bytes);
    }
    
    public static bool operator ==(Hash32 left, Hash32 right) => left.Equals(right);
    public static bool operator !=(Hash32 left, Hash32 right) => !left.Equals(right);
    
    public override bool Equals(object? obj) => obj is Hash32 other && Equals(other);
    
    public bool Equals(Hash32 other) => _h0 == other._h0 && _h1 == other._h1 && _h2 == other._h2 && _h3 == other._h3;
    public int CompareTo(Hash32 other)
    {
        var c = _h0.CompareTo(other._h0);
        if (c != 0) return c;
        c = _h1.CompareTo(other._h1);
        if (c != 0) return c;
        c = _h2.CompareTo(other._h2);
        if (c != 0) return c;
        return _h3.CompareTo(other._h3);
    }

    public override int GetHashCode() => HashCode.Combine(_h0,_h1,_h2,_h3);
    
    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < 32) throw new ArgumentException("Destination too small", nameof(destination));

        BinaryPrimitives.WriteUInt64LittleEndian(destination[0..8], _h0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..16], _h1);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..24], _h2);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..32], _h3);
    }
    
    public byte[] GetBytes()
    {
        var bytes = GC.AllocateUninitializedArray<byte>(32);
        WriteBytes(bytes);
        return bytes;
    }
    
    public string ToHexString()
    {
        Span<byte> bytes = stackalloc byte[32];
        WriteBytes(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}

public sealed class Hash32TypeConverter : JsonConverter<Hash32>
{
    public override Hash32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hex = reader.GetString();
        if (hex == null) throw new JsonException("Expected string for Hash32");
        return Hash32.FromHexString(hex);
    }

    public override void Write(Utf8JsonWriter writer, Hash32 value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToHexString());
    }
}
