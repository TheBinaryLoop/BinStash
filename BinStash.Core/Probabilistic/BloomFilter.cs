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

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BinStash.Core.Probabilistic;

// Bloom filter optimized for checking presence/absence of a BLAKE3 hash (byte[]/span).
// Uses bit-packed ulong[] and power-of-two bit count for fast masking.
public sealed class BloomFilter
{
    private readonly ulong[] _bits;
    private readonly int _bitCount;         // m (rounded up to power of two)
    private readonly int _hashFunctionCount; // k
    private readonly int _mask;             // m - 1
    
    // ---- Format header (little-endian): [Magic(4)] [Version(1)] [BitCount(int32)] [K(int32)] [Ulongs...]
    private const uint Magic = 0x4D4C4642;   // "BFLM" (Bloom FiLter Magic)
    private const byte Version = 1;

    public int BitCount => _bitCount;
    public int HashFunctionCount => _hashFunctionCount;

    /// <summary>
    /// Creates a new Bloom filter, specifying an error rate of 1/capacity,
    /// using the optimal size for the underlying data structure based on the desired capacity and error rate,
    /// as well as the optimal number of hash functions.
    /// </summary>
    /// <param name="capacity">The anticipated number of items to be added to the filter.</param>
    public BloomFilter(int capacity)
        : this(capacity, BestErrorRate(capacity))
    { }

    /// <summary>
    /// Creates a new Bloom filter, using the optimal size for the underlying data structure based on the desired
    /// capacity and error rate, as well as the optimal number of hash functions.
    /// </summary>
    /// <param name="capacity">The anticipated number of items to be added to the filter.</param>
    /// <param name="errorRate">The acceptable false-positive rate (e.g., 0.01F = 1%)</param>
    public BloomFilter(int capacity, float errorRate)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be > 0");
        if (errorRate is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(errorRate), "errorRate must be between 0 and 1, exclusive.");

        // Standard formulas:
        // m = - (n * ln(p)) / (ln(2)^2)
        // k = (m / n) * ln(2)
        var mOptimal = capacity * Math.Log(errorRate) / -(Math.Log(2) * Math.Log(2));
        var m = checked((int)Math.Ceiling(mOptimal));

        // Round up to power of two so modulo can be a mask
        var mPow2 = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(m, 64)); // minimum 64 bits
        _bitCount = mPow2;
        _mask = mPow2 - 1;

        var kOptimal = (int)Math.Round((_bitCount / (double)capacity) * Math.Log(2.0));
        _hashFunctionCount = Math.Max(1, kOptimal);

        _bits = new ulong[_bitCount >> 6]; // divide by 64
    }
    
    // Internal ctor for deserialization
    private BloomFilter(int bitCountPow2, int hashFunctionCount, ulong[] bits)
    {
        if (bitCountPow2 <= 0 || (bitCountPow2 & (bitCountPow2 - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(bitCountPow2), "Bit count must be a power of two and > 0.");
        if ((bitCountPow2 >> 6) != bits.Length)
            throw new ArgumentException("bits length does not match bitCount.", nameof(bits));
        if (hashFunctionCount < 1)
            throw new ArgumentOutOfRangeException(nameof(hashFunctionCount));

        _bitCount = bitCountPow2;
        _mask = bitCountPow2 - 1;
        _hashFunctionCount = hashFunctionCount;
        _bits = bits;
    }

    /// <summary>
    /// The ratio of false to true bits in the filter. E.g., 1 true bit in a 10 bit filter means a truthiness of 0.1.
    /// </summary>
    public double Truthiness
    {
        get
        {
            var ones = _bits.Aggregate<ulong, long>(0, (current, w) => current + BitOperations.PopCount(w));
            return (double)ones / _bitCount;
        }
    }

    /// <summary>
    /// Adds a new item to the filter. It cannot be removed.
    /// </summary>
    /// <param name="hash">The hash digest; must be at least 16 bytes (uses the first 16 bytes).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ReadOnlySpan<byte> hash)
    {
        var (h1, h2) = GetDoubleHash(hash);
        for (var i = 0; i < _hashFunctionCount; i++)
        {
            // Performs Dillinger and Manolios double hashing.
            var idx = (int)((h1 + (ulong)i * h2) & (uint)_mask);
            SetBit(idx);
        }
    }

    /// <summary>
    /// Checks for the existence of the item in the filter for a given probability.
    /// </summary>
    /// <param name="hash">The hash digest; must be at least 16 bytes (uses the first 16 bytes).</param>
    /// <returns>true if the item may be in the set; false if definitely not.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(ReadOnlySpan<byte> hash)
    {
        var (h1, h2) = GetDoubleHash(hash);
        for (var i = 0; i < _hashFunctionCount; i++)
        {
            var idx = (int)((h1 + (ulong)i * h2) & (uint)_mask);
            if (!GetBit(idx)) return false;
        }
        return true;
    }
    
    // ------------------ Serialization API ------------------

    /// <summary>
    /// Serializes the filter into a compact little-endian byte[] for storage.
    /// Layout: Magic[4], Version[1], BitCount[int32], K[int32], Bits[ulong * (BitCount/64)].
    /// </summary>
    public byte[] ToByteArray()
    {
        checked
        {
            var wordCount = _bits.Length;
            var payloadBytes = wordCount * sizeof(ulong);
            var totalSize = 4 + 1 + 4 + 4 + payloadBytes;

            var buffer = new byte[totalSize];
            var span = buffer.AsSpan();

            BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);    // magic
            span[4] = Version;                                        // version
            BinaryPrimitives.WriteInt32LittleEndian(span[5..], _bitCount);
            BinaryPrimitives.WriteInt32LittleEndian(span[9..], _hashFunctionCount);

            var bitsDest = span[13..];
            var o = 0;
            for (var i = 0; i < wordCount; i++, o += 8)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bitsDest.Slice(o, 8), _bits[i]);
            }
            return buffer;
        }
    }

    /// <summary>
    /// Restores a filter from a byte[] created by <see cref="ToByteArray"/>.
    /// Throws on malformed input.
    /// </summary>
    public static BloomFilter FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13) throw new ArgumentException("Data too short.", nameof(data));

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (magic != Magic) throw new FormatException("Invalid BloomFilter magic.");

        var version = data[4];
        if (version != Version) throw new NotSupportedException($"Unsupported BloomFilter version {version}.");

        var bitCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(5));
        var k = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(9));
        if (bitCount <= 0 || (bitCount & (bitCount - 1)) != 0)
            throw new FormatException("BitCount is not a power of two.");

        var words = bitCount >> 6;
        var expected = 13 + (words * 8);
        if (data.Length != expected) throw new FormatException("Payload length does not match header.");

        var bits = new ulong[words];
        var src = data[13..];
        var o = 0;
        for (var i = 0; i < words; i++, o += 8)
        {
            bits[i] = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(o, 8));
        }

        return new BloomFilter(bitCount, k, bits);
    }

    /// <summary>
    /// Tries to restore a filter from a byte[] created by <see cref="ToByteArray"/>.
    /// Returns false on malformed input instead of throwing.
    /// </summary>
    public static bool TryFromBytes(ReadOnlySpan<byte> data, [NotNullWhen(true)]out BloomFilter? filter)
    {
        try
        {
            filter = FromBytes(data);
            return true;
        }
        catch
        {
            filter = null;
            return false;
        }
    }

    
    public string DebugBloom()
    {
        var ones = 0L; 
        var zeroWords = 0;
        foreach (var w in _bits)
        {
            if (w==0) zeroWords++;
            ones += BitOperations.PopCount(w);
        }
        return $"BF: m={_bitCount} k={_hashFunctionCount} truthiness={(double)ones/_bitCount:P3} zeroWords={zeroWords}";
    }

    
    // ----- internals ---------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (ulong h1, ulong h2) GetDoubleHash(scoped ReadOnlySpan<byte> hash)
    {
        if (hash.Length < 16)
            throw new ArgumentException("Need at least 16 bytes of hash input (use BLAKE3-16 or larger).", nameof(hash));

        // Little-endian load of two 64-bit values from the first 16 bytes
        var h1 = UnsafeReadUInt64Le(hash);
        var h2 = UnsafeReadUInt64Le(hash.Slice(8));

        // Make sure h2 is odd to avoid degenerate stepping
        h2 |= 1UL;
        return (h1, h2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong UnsafeReadUInt64Le(ReadOnlySpan<byte> src)
    {
        // Equivalent to BinaryPrimitives.ReadUInt64LittleEndian but inlined for speed
        return src[0]
             | ((ulong)src[1] << 8)
             | ((ulong)src[2] << 16)
             | ((ulong)src[3] << 24)
             | ((ulong)src[4] << 32)
             | ((ulong)src[5] << 40)
             | ((ulong)src[6] << 48)
             | ((ulong)src[7] << 56);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBit(int index)
    {
        var word = index >> 6;
        var bit = index & 63;
        _bits[word] |= 1UL << bit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetBit(int index)
    {
        var word = index >> 6;
        var bit = index & 63;
        return ((_bits[word] >> bit) & 1UL) != 0;
    }

    /// <summary>
    /// The best error rate.
    /// </summary>
    /// <param name="capacity"> The capacity. </param>
    /// <returns> The <see cref="float"/>. </returns>
    private static float BestErrorRate(int capacity)
    {
        // Preserve the original behavior:
        // default error rate = 1/capacity (bounded to something sensible if capacity is huge)
        var c = (float)(1.0 / capacity);
        if (c > 0) return c;

        // Fallback for extreme values
        return 1e-9f;
    }
}
