// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using BinStash.Contracts.Hashing;

namespace BinStash.Infrastructure.Storage.Indexing;

/// <summary>
/// A space-efficient probabilistic membership filter for BLAKE3 chunk hashes.
///
/// <para>
/// <strong>Algorithm:</strong> Classic double-hashing bloom filter.  Both
/// hash functions are derived from the first 16 bytes of the BLAKE3 hash
/// itself — h1 from bytes 0..7 (little-endian uint64), h2 from bytes 8..15
/// (little-endian uint64).  No secondary hashing is performed: BLAKE3 output
/// bytes are uniformly distributed and can be consumed directly as
/// independent hash streams.
/// </para>
///
/// <para>
/// <strong>False-positive rate:</strong> Targeting 0.1% (1 in 1000) at
/// capacity.  For <c>n</c> expected elements:
/// <list type="bullet">
///   <item>Optimal bit count  m = ceil(-n * ln(0.001) / ln(2)^2) ≈ 14.4 × n, rounded up to next byte boundary.</item>
///   <item>Optimal hash count k = ceil((m / n) * ln(2)) ≈ 10.</item>
/// </list>
/// At 65,536 entries  → ~940 KB filter.
/// At 244,000 entries → ~3.5 MB filter.
/// </para>
///
/// <para>
/// <strong>On-disk format (little-endian):</strong>
/// <code>
/// [4 bytes] BitCount  : uint32  — number of bits in the filter
/// [4 bytes] HashCount : uint32  — number of hash rounds (k)
/// [BitCount/8 bytes]  — raw filter bits, LSB-first per byte
/// </code>
/// </para>
/// </summary>
internal sealed class PackIndexBloomFilter
{
    private const double TargetFpr = 0.001; // 0.1 %
    private const double Ln2 = 0.6931471805599453;
    private const double Ln2Sq = Ln2 * Ln2;

    private readonly uint[] _bits;
    private readonly int _bitCount;
    private readonly int _hashCount;

    // -----------------------------------------------------------------------
    // Construction

    /// <summary>Creates a new mutable filter sized for <paramref name="expectedEntries"/> entries.</summary>
    public PackIndexBloomFilter(int expectedEntries)
    {
        if (expectedEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedEntries), "Must be > 0.");

        // m = ceil(- n * ln(fpr) / ln(2)^2)
        var rawBits = (int)Math.Ceiling(-expectedEntries * Math.Log(TargetFpr) / Ln2Sq);
        // Round up to a multiple of 32 for uint[] alignment
        _bitCount = ((rawBits + 31) / 32) * 32;

        // k = ceil((m/n) * ln(2))
        _hashCount = (int)Math.Max(1, Math.Ceiling(((double)_bitCount / expectedEntries) * Ln2));

        _bits = new uint[_bitCount / 32];
    }

    /// <summary>Deserializes a bloom filter from its on-disk representation.</summary>
    private PackIndexBloomFilter(int bitCount, int hashCount, uint[] bits)
    {
        _bitCount = bitCount;
        _hashCount = hashCount;
        _bits = bits;
    }

    // -----------------------------------------------------------------------
    // Mutate

    /// <summary>Adds <paramref name="hash"/> to the filter.</summary>
    public void Add(Hash32 hash)
    {
        GetHashPair(hash, out var h1, out var h2);
        for (var i = 0; i < _hashCount; i++)
        {
            var bit = (int)((h1 + (ulong)i * h2) % (ulong)_bitCount);
            _bits[bit >> 5] |= 1u << (bit & 31);
        }
    }

    // -----------------------------------------------------------------------
    // Query

    /// <summary>
    /// Returns <see langword="false"/> if <paramref name="hash"/> is
    /// <em>definitely not</em> in the set.  Returns <see langword="true"/> if
    /// it <em>might</em> be (subject to the configured false-positive rate).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MightContain(Hash32 hash)
    {
        GetHashPair(hash, out var h1, out var h2);
        for (var i = 0; i < _hashCount; i++)
        {
            var bit = (int)((h1 + (ulong)i * h2) % (ulong)_bitCount);
            if ((_bits[bit >> 5] & (1u << (bit & 31))) == 0)
                return false;
        }
        return true;
    }

    // -----------------------------------------------------------------------
    // Serialization

    /// <summary>
    /// Serializes the filter to a byte array suitable for writing to a
    /// <c>.bloom</c> file.
    /// </summary>
    public byte[] Serialize()
    {
        // Header: 4 bytes BitCount + 4 bytes HashCount
        // Body:   _bits.Length × 4 bytes (uint[] little-endian)
        var buf = new byte[8 + _bits.Length * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), (uint)_bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), (uint)_hashCount);
        for (var i = 0; i < _bits.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8 + i * 4, 4), _bits[i]);
        return buf;
    }

    /// <summary>
    /// Deserializes a bloom filter from the bytes produced by
    /// <see cref="Serialize"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown if the byte array is too short or the declared size is
    /// inconsistent.
    /// </exception>
    public static PackIndexBloomFilter Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("Bloom filter data too short (< 8 bytes).");

        var bitCount  = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[0..4]);
        var hashCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);

        if (bitCount <= 0 || bitCount % 32 != 0)
            throw new InvalidDataException($"Invalid bloom filter bit count: {bitCount}.");

        var wordCount = bitCount / 32;
        var expectedLen = 8 + wordCount * 4;

        if (data.Length < expectedLen)
            throw new InvalidDataException($"Bloom filter truncated: expected {expectedLen} bytes, got {data.Length}.");

        var bits = new uint[wordCount];
        for (var i = 0; i < wordCount; i++)
            bits[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8 + i * 4, 4));

        return new PackIndexBloomFilter(bitCount, hashCount, bits);
    }

    // -----------------------------------------------------------------------
    // Internal helpers

    /// <summary>
    /// Extracts the two independent 64-bit hash values used for
    /// double-hashing directly from the first 16 bytes of the BLAKE3 digest.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetHashPair(Hash32 hash, out ulong h1, out ulong h2)
    {
        // WriteBytes outputs the four internal ulongs as little-endian bytes
        // in the order h0, h1, h2, h3.  We read the first two as h1/h2 for
        // the double-hash scheme — they are independent and uniformly
        // distributed by BLAKE3's design.
        Span<byte> buf = stackalloc byte[32];
        hash.WriteBytes(buf);
        h1 = BinaryPrimitives.ReadUInt64LittleEndian(buf[..8]);
        h2 = BinaryPrimitives.ReadUInt64LittleEndian(buf[8..16]);
        // Ensure h2 is odd to satisfy the mathematical requirements of the
        // double-hash scheme (h2 must be coprime with m; odd h2 + power-of-2 m
        // guarantees full period).
        h2 |= 1;
    }
}
