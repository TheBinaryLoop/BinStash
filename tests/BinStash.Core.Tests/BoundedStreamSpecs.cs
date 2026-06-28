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

using BinStash.Core.IO;
using FluentAssertions;

namespace BinStash.Core.Tests;

public class BoundedStreamSpecs
{
    private static BoundedStream Wrap(byte[] data, long limit)
        => new(new MemoryStream(data), limit);

    // ---- Capabilities -------------------------------------------------------

    [Fact]
    public void CanRead_is_true()
    {
        Wrap(new byte[4], 4).CanRead.Should().BeTrue();
    }

    [Fact]
    public void CanSeek_is_false()
    {
        Wrap(new byte[4], 4).CanSeek.Should().BeFalse();
    }

    [Fact]
    public void CanWrite_is_false()
    {
        Wrap(new byte[4], 4).CanWrite.Should().BeFalse();
    }

    [Fact]
    public void Length_reflects_declared_limit()
    {
        Wrap(new byte[10], 5).Length.Should().Be(5);
    }

    [Fact]
    public void Initial_position_is_zero()
    {
        Wrap(new byte[4], 4).Position.Should().Be(0);
    }

    // ---- Unsupported ops throw ----------------------------------------------

    [Fact]
    public void Setting_Position_throws()
    {
        var s = Wrap(new byte[4], 4);
        var act = () => s.Position = 0;
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Seek_throws()
    {
        var s = Wrap(new byte[4], 4);
        var act = () => s.Seek(0, SeekOrigin.Begin);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SetLength_throws()
    {
        var s = Wrap(new byte[4], 4);
        var act = () => s.SetLength(4);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Write_throws()
    {
        var s = Wrap(new byte[4], 4);
        var act = () => s.Write(new byte[1], 0, 1);
        act.Should().Throw<NotSupportedException>();
    }

    // ---- Read (byte[]) -------------------------------------------------------

    [Fact]
    public void Read_returns_all_bytes_within_limit()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var s = Wrap(data, 5);
        var buf = new byte[5];
        var read = s.Read(buf, 0, 5);
        read.Should().Be(5);
        buf.Should().Equal(data);
    }

    [Fact]
    public void Read_stops_at_limit_even_when_more_bytes_exist_in_underlying_stream()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var s = Wrap(data, 3);
        var buf = new byte[5];
        var read = s.Read(buf, 0, 5);
        read.Should().Be(3);
        buf[..3].Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Read_returns_zero_after_limit_exhausted()
    {
        var data = new byte[] { 1, 2 };
        var s = Wrap(data, 2);
        var buf1 = new byte[2];
        s.ReadExactly(buf1, 0, 2); // consume all
        var buf2 = new byte[2];
        s.Read(buf2, 0, 2).Should().Be(0);
    }

    [Fact]
    public void Position_advances_after_reads()
    {
        var data = new byte[] { 10, 20, 30 };
        var s = Wrap(data, 3);
        var buf = new byte[2];
        s.ReadExactly(buf, 0, 2);
        s.Position.Should().Be(2);
    }

    // ---- Read (Span<byte>) ---------------------------------------------------

    [Fact]
    public void Span_read_returns_all_bytes_within_limit()
    {
        var data = new byte[] { 9, 8, 7 };
        var s = Wrap(data, 3);
        Span<byte> span = new byte[3];
        s.Read(span).Should().Be(3);
        span.ToArray().Should().Equal(data);
    }

    [Fact]
    public void Span_read_stops_at_limit()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var s = Wrap(data, 2);
        Span<byte> span = new byte[5];
        var read = s.Read(span);
        read.Should().Be(2);
        span[..2].ToArray().Should().Equal(1, 2);
    }

    [Fact]
    public void Span_read_returns_zero_after_limit_exhausted()
    {
        var data = new byte[] { 1, 2 };
        var s = Wrap(data, 2);
        Span<byte> buf = new byte[2];
        s.ReadExactly(buf);
        s.Read(buf).Should().Be(0);
    }

    // ---- Multiple partial reads accumulate position -------------------------

    [Fact]
    public void Multiple_reads_accumulate_position_correctly()
    {
        var data = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();
        var s = Wrap(data, 10);
        var buf = new byte[3];
        s.ReadExactly(buf, 0, 3); // pos = 3
        s.ReadExactly(buf, 0, 3); // pos = 6
        s.Position.Should().Be(6);
        s.BitsRemaining().Should().Be(4); // 10 - 6 = 4 bytes remain
    }

    // ---- Flush is no-op and does not throw ----------------------------------

    [Fact]
    public void Flush_does_not_throw()
    {
        var s = Wrap(new byte[4], 4);
        var act = () => s.Flush();
        act.Should().NotThrow();
    }

    // ---- Zero-length limit --------------------------------------------------

    [Fact]
    public void Zero_length_limit_always_returns_zero_from_read()
    {
        var data = new byte[] { 1, 2, 3 };
        var s = Wrap(data, 0);
        s.Read(new byte[3], 0, 3).Should().Be(0);
        s.Length.Should().Be(0);
    }
}

// Helper to access remaining byte count without exposing internals as a property
file static class BoundedStreamTestExtensions
{
    public static long BitsRemaining(this Stream s) => s.Length - s.Position;
}
