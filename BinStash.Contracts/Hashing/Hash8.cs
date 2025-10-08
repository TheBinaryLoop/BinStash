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

namespace BinStash.Contracts.Hashing;

public readonly struct Hash8 : IEquatable<Hash8>, IComparable<Hash8>
{
    private readonly ulong _h0;

    
    public Hash8(ulong hash)
    {
        _h0 = hash;
    }
    
    public Hash8(byte[] bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(bytes.Length, 8);
        _h0 = BitConverter.ToUInt64(bytes, 0);
    }
    
    public Hash8(ReadOnlySpan<byte> bytes)
    {
        /* pack 4*8 little-endian */
        ArgumentOutOfRangeException.ThrowIfNotEqual(bytes.Length, 8);
        _h0 = BitConverter.ToUInt64(bytes);
    }
    
    public static Hash8 FromHexString(string hex)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(hex.Length, 16);
        var bytes = Convert.FromHexString(hex);
        return new Hash8(bytes);
    }
    
    public bool Equals(Hash8 other) => _h0 == other._h0;

    public int CompareTo(Hash8 other) => _h0.CompareTo(other._h0);
    
    public override int GetHashCode() => _h0.GetHashCode();

    public byte[] GetBytes()
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes, _h0);
        return bytes;
    }
    
    public string ToHexString() => Convert.ToHexStringLower(GetBytes());
}