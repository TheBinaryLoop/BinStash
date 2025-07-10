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

internal class BitWriter
{
    private readonly List<byte> _Buffer = new();
    private byte _Current;
    private int _BitCount;

    public void WriteBits(ulong value, int bitCount)
    {
        for (var i = 0; i < bitCount; i++)
        {
            var bit = (value >> i) & 1;
            _Current |= (byte)(bit << _BitCount++);
            if (_BitCount != 8) continue;
            _Buffer.Add(_Current);
            _Current = 0;
            _BitCount = 0;
        }
    }

    public byte[] ToArray()
    {
        if (_BitCount > 0) _Buffer.Add(_Current);
        return _Buffer.ToArray();
    }
}