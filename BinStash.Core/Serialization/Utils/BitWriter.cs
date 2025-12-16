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

namespace BinStash.Core.Serialization.Utils;

internal class BitWriter
{
    private readonly List<byte> _buffer = new();
    private byte _current;
    private int _bitCount; // number of bits already filled in _current (0..7)

    /// <summary>
    /// Writes the lowest <paramref name="bitCount"/> bits of <paramref name="value"/> LSB-first.
    /// The first bit written goes into bit position 0 of the current byte.
    /// </summary>
    public void WriteBits(ulong value, int bitCount)
    {
        if ((uint)bitCount > 64u)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "bitCount must be between 0 and 64.");

        // Optional debug safety: ensure callers don't pass bits that would be truncated.
        if (bitCount < 64 && (value >> bitCount) != 0) 
            throw new ArgumentException("Value has set bits outside bitCount.", nameof(value));

        var remaining = bitCount;
        var shift = 0; // how many bits of 'value' we have already consumed

        // Fill up current partial byte if any
        if (_bitCount != 0 && remaining > 0)
        {
            var space = 8 - _bitCount; // free bits in _current
            var take = remaining < space ? remaining : space;

            // Take 'take' LSBs from value
            var chunk = (byte)((value >> shift) & ((1u << take) - 1u));
            _current |= (byte)(chunk << _bitCount);

            _bitCount += take;
            shift += take;
            remaining -= take;

            if (_bitCount == 8)
            {
                _buffer.Add(_current);
                _current = 0;
                _bitCount = 0;
            }
        }

        // Write full bytes directly from value
        while (remaining >= 8)
        {
            var b = (byte)((value >> shift) & 0xFFu);
            _buffer.Add(b);
            shift += 8;
            remaining -= 8;
        }

        // Write tail bits into _current
        if (remaining > 0)
        {
            var tail = (byte)((value >> shift) & ((1u << remaining) - 1u));
            _current |= (byte)(tail << _bitCount);
            _bitCount += remaining;

            if (_bitCount == 8)
            {
                _buffer.Add(_current);
                _current = 0;
                _bitCount = 0;
            }
        }
    }

    public byte[] ToArray()
    {
        if (_bitCount > 0)
        {
            _buffer.Add(_current);
            _current = 0;
            _bitCount = 0;
        }

        return _buffer.ToArray();
    }
}