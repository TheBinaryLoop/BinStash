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

using BinStash.Contracts.Hashing;
using BinStash.Core.Compression;
using FluentAssertions;

namespace BinStash.Core.Tests;

public class ChecksumCompressorSpecs
{
    // ---- helpers ------------------------------------------------------------

    private static byte[] MakeHash(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    private static Hash32 MakeHash32(byte fill) => new(MakeHash(fill));

    private static List<byte[]> MakeHashList(params byte[] fills)
        => fills.Select(MakeHash).ToList();

    private static List<Hash32> MakeHash32List(params byte[] fills)
        => fills.Select(MakeHash32).ToList();

    // ---- TransposeCompress: empty list --------------------------------------

    [Fact]
    public void Empty_list_produces_varint_zero_output()
    {
        var result = ChecksumCompressor.TransposeCompress(new List<byte[]>());
        result.Should().NotBeNull();
        result.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Empty_list_round_trips_to_empty_list_via_bytes_overload()
    {
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]>());
        var decompressed = ChecksumCompressor.TransposeDecompressHashes(compressed);
        decompressed.Should().BeEmpty();
    }

    // ---- TransposeCompress: wrong hash size ---------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Hash_of_wrong_size_throws_InvalidDataException(int badSize)
    {
        var hashes = new List<byte[]> { new byte[badSize] };
        var act = () => ChecksumCompressor.TransposeCompress(hashes);
        act.Should().Throw<InvalidDataException>().WithMessage("*32*");
    }

    // ---- Round-trip: TransposeCompress → TransposeDecompressHashes (bytes) --

    [Fact]
    public void Single_hash_round_trips_via_bytes_overload()
    {
        var hash = MakeHash(0xAB);
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]> { hash });
        var result = ChecksumCompressor.TransposeDecompressHashes(compressed);

        result.Should().HaveCount(1);
        result[0].Should().Be(new Hash32(hash));
    }

    [Fact]
    public void Multiple_distinct_hashes_round_trip_via_bytes_overload()
    {
        var input = MakeHashList(0x01, 0x02, 0x03, 0x04, 0x05);
        var compressed = ChecksumCompressor.TransposeCompress(input);
        var result = ChecksumCompressor.TransposeDecompressHashes(compressed);

        result.Should().HaveCount(5);
        for (var i = 0; i < input.Count; i++)
            result[i].Should().Be(new Hash32(input[i]));
    }

    [Fact]
    public void Identical_hashes_round_trip_correctly()
    {
        var hash = MakeHash(0xFF);
        var input = new List<byte[]> { hash, hash, hash };
        var compressed = ChecksumCompressor.TransposeCompress(input);
        var result = ChecksumCompressor.TransposeDecompressHashes(compressed);

        result.Should().HaveCount(3);
        foreach (var h in result)
            h.Should().Be(new Hash32(hash));
    }

    [Fact]
    public void All_zero_hash_round_trips_correctly()
    {
        var hash = new byte[32]; // all zeros
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]> { hash });
        var result = ChecksumCompressor.TransposeDecompressHashes(compressed);

        result.Should().HaveCount(1);
        result[0].Should().Be(new Hash32(hash));
    }

    [Fact]
    public void All_max_byte_hash_round_trips_correctly()
    {
        var hash = Enumerable.Repeat((byte)0xFF, 32).ToArray();
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]> { hash });
        var result = ChecksumCompressor.TransposeDecompressHashes(compressed);

        result.Should().HaveCount(1);
        result[0].Should().Be(new Hash32(hash));
    }

    [Fact]
    public void Large_number_of_hashes_round_trips_correctly()
    {
        var rng = new Random(42);
        var input = new List<byte[]>();
        for (var i = 0; i < 1000; i++)
        {
            var b = new byte[32];
            rng.NextBytes(b);
            input.Add(b);
        }

        var compressed = ChecksumCompressor.TransposeCompress(input);
        var result = ChecksumCompressor.TransposeDecompressHashes(compressed);

        result.Should().HaveCount(1000);
        for (var i = 0; i < input.Count; i++)
            result[i].Should().Be(new Hash32(input[i]));
    }

    // ---- Round-trip: TransposeCompress → TransposeDecompressHashes (stream) --

    [Fact]
    public void Single_hash_round_trips_via_stream_overload()
    {
        var hash = MakeHash(0x55);
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]> { hash });
        using var ms = new MemoryStream(compressed);
        var result = ChecksumCompressor.TransposeDecompressHashes(ms);

        result.Should().HaveCount(1);
        result[0].Should().Be(new Hash32(hash));
    }

    [Fact]
    public void Multiple_hashes_round_trip_via_stream_overload()
    {
        var input = MakeHashList(0x10, 0x20, 0x30);
        var compressed = ChecksumCompressor.TransposeCompress(input);
        using var ms = new MemoryStream(compressed);
        var result = ChecksumCompressor.TransposeDecompressHashes(ms);

        result.Should().HaveCount(3);
        for (var i = 0; i < input.Count; i++)
            result[i].Should().Be(new Hash32(input[i]));
    }

    // ---- Round-trip: async path --------------------------------------------

    [Fact]
    public async Task Single_hash_round_trips_via_async_overload()
    {
        var hash = MakeHash(0xCC);
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]> { hash });
        using var ms = new MemoryStream(compressed);
        var result = await ChecksumCompressor.TransposeDecompressHashesAsync(ms);

        result.Should().HaveCount(1);
        result[0].Should().Be(new Hash32(hash));
    }

    [Fact]
    public async Task Empty_list_round_trips_via_async_overload()
    {
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]>());
        using var ms = new MemoryStream(compressed);
        var result = await ChecksumCompressor.TransposeDecompressHashesAsync(ms);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Multiple_hashes_round_trip_via_async_overload()
    {
        var input = MakeHashList(0x01, 0x80, 0xFF);
        var compressed = ChecksumCompressor.TransposeCompress(input);
        using var ms = new MemoryStream(compressed);
        var result = await ChecksumCompressor.TransposeDecompressHashesAsync(ms);

        result.Should().HaveCount(3);
        for (var i = 0; i < input.Count; i++)
            result[i].Should().Be(new Hash32(input[i]));
    }

    // ---- Backward-compat wrapper: TransposeDecompress ----------------------

    [Fact]
    public void TransposeDecompress_returns_raw_byte_arrays()
    {
        var input = MakeHashList(0xAA, 0xBB);
        var compressed = ChecksumCompressor.TransposeCompress(input);
        using var ms = new MemoryStream(compressed);
        var result = ChecksumCompressor.TransposeDecompress(ms);

        result.Should().HaveCount(2);
        result[0].Should().Equal(input[0]);
        result[1].Should().Equal(input[1]);
    }

    // ---- Compression actually reduces or handles well-known byte patterns ---

    [Fact]
    public void Compressed_output_is_non_empty_for_single_hash()
    {
        var hash = MakeHash(0x77);
        var compressed = ChecksumCompressor.TransposeCompress(new List<byte[]> { hash });
        compressed.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Order_of_hashes_is_preserved_after_round_trip()
    {
        var input = Enumerable.Range(0, 32).Select(i => MakeHash((byte)i)).ToList();
        var compressed = ChecksumCompressor.TransposeCompress(input);
        var result = ChecksumCompressor.TransposeDecompressHashes(compressed);

        result.Should().HaveCount(32);
        for (var i = 0; i < input.Count; i++)
            result[i].Should().Be(new Hash32(input[i]));
    }
}
