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

namespace BinStash.Core.Tests;

public static class TestUtils
{
    public static string TempFileWith(ReadOnlySpan<byte> data)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binstash-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    public static byte[] RandomBytes(int size, int seed = 123)
    {
        var rng = new Random(seed);
        var b = new byte[size];
        rng.NextBytes(b);
        return b;
    }

    public static byte[] RepeatingPattern(int size, ReadOnlySpan<byte> pattern)
    {
        var buf = new byte[size];
        for (int i = 0; i < size; i++) buf[i] = pattern[i % pattern.Length];
        return buf;
    }

    public static byte[] WithInsertion(byte[] src, int at, byte[] insert)
    {
        var dst = new byte[src.Length + insert.Length];
        Buffer.BlockCopy(src, 0, dst, 0, at);
        Buffer.BlockCopy(insert, 0, dst, at, insert.Length);
        Buffer.BlockCopy(src, at, dst, at + insert.Length, src.Length - at);
        return dst;
    }

    public static byte[] WithBitFlip(byte[] src, int at, byte mask = 0x01)
    {
        var dst = (byte[])src.Clone();
        dst[at] ^= mask;
        return dst;
    }
}
