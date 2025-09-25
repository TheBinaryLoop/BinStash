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

namespace BinStash.Core.Collections;

internal sealed class ByteArrayComparer : IEqualityComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance = new();
    public bool Equals(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Length != y.Length) return false;
        for (var i=0; i<x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }
    public int GetHashCode(byte[] obj)
    {
        // FNV-1a
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var b in obj) hash = (hash ^ b) * 16777619;
            return hash;
        }
    }
}
