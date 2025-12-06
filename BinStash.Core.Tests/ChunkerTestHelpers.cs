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

using BinStash.Core.Chunking;
using FluentAssertions;

namespace BinStash.Core.Tests;

public static class ChunkerTestHelpers
{
    public static byte[] RandomBytes(int length, int seed = 123)
    {
        var rng = new Random(seed);
        var data = new byte[length];
        rng.NextBytes(data);
        return data;
    }

    public static MemoryStream AsStream(this byte[] bytes) => new(bytes, writable: false);

    public static void AssertPartitionIsValid(IReadOnlyList<ChunkMapEntry> map, long totalLength)
    {
        map.Should().NotBeNull();
        if (totalLength == 0) { map.Should().BeEmpty(); return; }

        map.Should().NotBeEmpty();
        map.First().Offset.Should().Be(0);
        long pos = 0;
        foreach (var m in map)
        {
            m.Offset.Should().Be(pos);
            m.Length.Should().BePositive();
            pos += m.Length;
        }
        pos.Should().Be(totalLength);
    }

    public static long CommonPrefixBytes(IReadOnlyList<ChunkMapEntry> a, IReadOnlyList<ChunkMapEntry> b)
    {
        var min = Math.Min(a.Count, b.Count);
        long bytes = 0;
        for (int i = 0; i < min; i++)
        {
            if (a[i].Offset == b[i].Offset && a[i].Length == b[i].Length) bytes += a[i].Length;
            else break;
        }
        return bytes;
    }

    public static long CommonSuffixBytes(IReadOnlyList<ChunkMapEntry> a, IReadOnlyList<ChunkMapEntry> b, long totalLen)
    {
        int ia = a.Count - 1, ib = b.Count - 1;
        long bytes = 0;
        while (ia >= 0 && ib >= 0)
        {
            if (a[ia].Length == b[ib].Length &&
                a[ia].Offset + a[ia].Length == totalLen &&
                b[ib].Offset + b[ib].Length == totalLen)
            {
                bytes += a[ia].Length;
                totalLen -= a[ia].Length;
                ia--; ib--;
            }
            else break;
        }
        return bytes;
    }
}