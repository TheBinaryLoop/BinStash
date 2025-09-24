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
    // Backing storage without mandatory copy:
    // - Prefer the byte[] ctor to avoid a copy.
    // - The ReadOnlySpan<byte> ctor keeps compatibility and copies once.
    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _length; // bytes available from _start
    private int _byteIndex;       // relative to _start
    private int _bitIndex;        // 0..7 within current byte

    /// <summary>Preferred: no copy.</summary>
    public BitReader(byte[] data) : this(data, 0, data.Length) { }

    /// <summary>No-copy with slice.</summary>
    public BitReader(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)offset, (uint)data.Length);
        if (length < 0 || offset + length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));
        _data = data;
        _start = offset;
        _length = length;
    }

    /// <summary>Compatibility: accepts a span but copies once.</summary>
    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray(); // keep original semantics
        _start = 0;
        _length = _data.Length;
    }

    /// <summary>Bits remaining in the packed buffer.</summary>
    public int BitsRemaining => (_length - _byteIndex) * 8 - _bitIndex;

    /// <summary>Total bytes consumed so far.</summary>
    public int BytesConsumed => _byteIndex + (_bitIndex > 0 ? 1 : 0);

    /// <summary>
    /// Reads the lowest <paramref name="bitCount"/> bits (0..64), LSB-first.
    /// The first bit read comes from bit position <c>_bitIndex</c> of the current byte.
    /// </summary>
    public ulong ReadBits(int bitCount)
    {
        if ((uint)bitCount > 64u)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "bitCount must be between 0 and 64.");
        if (bitCount == 0) return 0UL;

        if (bitCount > BitsRemaining)
            throw new EndOfStreamException($"Not enough bits: requested {bitCount}, have {BitsRemaining}.");

        ulong result = 0;
        var remaining = bitCount;
        var writeShift = 0; // where we place the next chunk in 'result'

        // If we're mid-byte, consume up to the end of the current byte.
        if (_bitIndex != 0)
        {
            var available = 8 - _bitIndex;
            var take = remaining < available ? remaining : available;

            // Extract 'take' bits starting at _bitIndex
            var cur = _data[_start + _byteIndex];
            var chunk = (uint)((cur >> _bitIndex) & ((1 << take) - 1));
            result |= (ulong)chunk << writeShift;

            _bitIndex += take;
            writeShift += take;
            remaining -= take;

            if (_bitIndex == 8)
            {
                _bitIndex = 0;
                _byteIndex++;
            }
        }

        // Read whole bytes fast
        while (remaining >= 8)
        {
            var b = _data[_start + _byteIndex];
            // Since stream is LSB-first, this byte contributes its 8 bits directly.
            result |= (ulong)b << writeShift;

            _byteIndex++;
            writeShift += 8;
            remaining -= 8;
        }

        // Tail bits (0..7)
        if (remaining > 0)
        {
            var cur = _data[_start + _byteIndex];
            var mask = (uint)((1 << remaining) - 1);
            var tail = cur & mask;
            result |= (ulong)tail << writeShift;

            _bitIndex = remaining; // we consumed 'remaining' LSBs of current byte
            if (_bitIndex == 8) // (cannot happen because remaining < 8)
            {
                _bitIndex = 0;
                _byteIndex++;
            }
        }

        return result;
    }
}
