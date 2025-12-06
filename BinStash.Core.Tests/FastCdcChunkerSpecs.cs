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

using BinStash.Contracts.Hashing;
using BinStash.Core.Chunking;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BinStash.Core.Tests;

public class FastCdcChunkerSpecs
{
    private static FastCdcChunker New(int min, int avg, int max) => new(min, avg, max);
    
    public static Arbitrary<byte[]> SmallByteArrays()
    {
        var byteGen = Gen.Choose(0, 255).Select(i => (byte)i);
        var lenGen = Gen.Choose(0, 200_000);
        
        var arrGen =
            from n in lenGen
            from arr in byteGen.ArrayOf(n)
            select arr;

        return arrGen.ToArbitrary();
    }

    [Fact]
    public void Empty_stream_yields_no_chunks()
    {
        var chunker = New(2*1024, 8*1024, 64*1024);
        using var ms = new MemoryStream([]);
        var map = chunker.GenerateChunkMap(ms);
        map.Should().BeEmpty();
    }

    [Fact]
    public void Small_stream_less_than_min_yields_single_chunk()
    {
        var chunker = New(8*1024, 16*1024, 64*1024);
        var data = new byte[4000]; // < min
        using var ms = new MemoryStream(data);
        var map = chunker.GenerateChunkMap(ms);

        map.Should().HaveCount(1);
        map[0].Offset.Should().Be(0);
        map[0].Length.Should().Be(data.Length);
    }

    [Fact]
    public async Task LoadChunkDataAsync_returns_exact_bytes_and_checksum()
    {
        var chunker = New(4*1024, 8*1024, 64*1024);
        var data = Enumerable.Range(0, 200_000).Select(b => (byte)(b*31)).ToArray();
        using var ms = new MemoryStream(data);

        var map = chunker.GenerateChunkMap(ms);
        map.Should().NotBeEmpty();

        // pick a middle chunk
        var middle = map[map.Count / 2];

        // Verify load from stream
        ms.Position = 0;
        var cd = await chunker.LoadChunkDataAsync(ms, middle);
        cd.Data.Length.Should().Be(middle.Length);
        cd.Data.Should().Equal(data.AsSpan((int)middle.Offset, middle.Length).ToArray());

        // Verify checksum matches Blake3(Hash32) recomputation
        var expected = new Hash32(Blake3.Hasher.Hash(cd.Data).AsSpan());
        cd.Checksum.Should().Be(expected);
    }

    [Fact]
    public void File_vs_Stream_generate_identical_maps()
    {
        var chunker = New(4*1024, 8*1024, 64*1024);
        var data = ChunkerTestHelpers.RandomBytes(500_000, seed: 42);
        var temp = Path.GetTempFileName();
        File.WriteAllBytes(temp, data);

        try
        {
            using var ms = data.AsStream();
            var mapStream = chunker.GenerateChunkMap(ms);
            var mapFile = chunker.GenerateChunkMap(temp);

            mapFile.Select(m => (m.Offset, m.Length, m.Checksum)).Should()
                   .Equal(mapStream.Select(m => (m.Offset, m.Length, m.Checksum)));
        }
        finally { File.Delete(temp); }
    }

    [Fact]
    public void Mmf_path_yields_valid_partition()
    {
        // Ensure > 16 MiB to trigger MemoryMappedFile path
        var chunker = New(32*1024, 64*1024, 512*1024);
        var data = ChunkerTestHelpers.RandomBytes(20 * 1024 * 1024, seed: 7);
        var temp = Path.GetTempFileName();
        File.WriteAllBytes(temp, data);

        try
        {
            var map = chunker.GenerateChunkMap(temp);
            ChunkerTestHelpers.AssertPartitionIsValid(map, data.Length);
            // sanity on sizes
            map.All(c => c.Length >= 32*1024 || data.Length < 32*1024).Should().BeTrue();
            map.All(c => c.Length <= 512*1024).Should().BeTrue();
        }
        finally { File.Delete(temp); }
    }
    
    [Property(Arbitrary = [typeof(FastCdcChunkerSpecs)], MaxTest = 200)]
    public void Partitions_cover_input_without_overlap(byte[] data)
    {
        var chunker = New(8*1024, 16*1024, 128*1024);
        using var ms = new MemoryStream(data);
        var map = chunker.GenerateChunkMap(ms);

        ChunkerTestHelpers.AssertPartitionIsValid(map, data.Length);
    }

    [Property(Arbitrary = [typeof(FastCdcChunkerSpecs)], MaxTest = 100)]
    public void Deterministic_for_same_bytes(byte[] data)
    {
        var chunker = New(8*1024, 16*1024, 128*1024);
        using var s1 = new MemoryStream(data);
        using var s2 = new MemoryStream(data.ToArray());

        var a = chunker.GenerateChunkMap(s1);
        var b = chunker.GenerateChunkMap(s2);

        a.Select(x => (x.Offset, x.Length, x.Checksum)).Should()
         .Equal(b.Select(x => (x.Offset, x.Length, x.Checksum)));
    }

    [Property(MaxTest = 50)]
    public void Average_chunk_size_is_near_target_on_random_data()
    {
        var min = 8*1024;
        var avg = 32*1024;
        var max = 256*1024;
        var chunker = New(min, avg, max);

        var data = ChunkerTestHelpers.RandomBytes(8 * 1024 * 1024, seed: 999); // 8 MiB
        using var ms = new MemoryStream(data);
        var map = chunker.GenerateChunkMap(ms);

        var mean = map.Count == 0 ? 0 : map.Average(c => c.Length);
        // Tolerance band: +/-50% of target average (tune per your masks)
        mean.Should().BeInRange(avg * 0.5, avg * 1.5);
        map.All(c => c.Length <= max).Should().BeTrue();
        // Allow the very last chunk to be < min if file shorter than min
        if (data.Length >= min) map.Take(map.Count - 1).All(c => c.Length >= min).Should().BeTrue();
    }

    [Property(MaxTest = 25)]
    public void Local_edit_affects_boundaries_locally()
    {
        var chunker = New(8*1024, 32*1024, 256*1024);

        var baseData = ChunkerTestHelpers.RandomBytes(2 * 1024 * 1024, seed: 2025);
        // insert 20 bytes in the middle
        var insertAt = baseData.Length / 2;
        var ins = Enumerable.Repeat((byte)0xAA, 20).ToArray();
        var edited = baseData.Take(insertAt).Concat(ins).Concat(baseData.Skip(insertAt)).ToArray();

        using var s1 = new MemoryStream(baseData);
        using var s2 = new MemoryStream(edited);

        var m1 = chunker.GenerateChunkMap(s1);
        var m2 = chunker.GenerateChunkMap(s2);

        // Heuristic: large common prefix/suffix vs total size
        var prefix = ChunkerTestHelpers.CommonPrefixBytes(m1, m2);
        var suffix = ChunkerTestHelpers.CommonSuffixBytes(m1, m2, Math.Min(baseData.Length, edited.Length));

        (prefix + suffix).Should().BeGreaterThan((long)(baseData.Length * 0.4)); // tune threshold if needed
    }
}