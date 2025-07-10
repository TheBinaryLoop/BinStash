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

internal sealed class BitReader
{
    private readonly byte[] _Data;
    private int _ByteIndex;
    private int _BitIndex;

    public BitReader(byte[] data) => _Data = data;

    public ulong ReadBits(int bitCount)
    {
        ulong result = 0;
        for (var i = 0; i < bitCount; i++)
        {
            var bit = (_Data[_ByteIndex] >> _BitIndex++) & 1;
            result |= ((ulong)bit << i);

            if (_BitIndex != 8) continue;
            _BitIndex = 0;
            _ByteIndex++;
        }
        return result;
    }
}