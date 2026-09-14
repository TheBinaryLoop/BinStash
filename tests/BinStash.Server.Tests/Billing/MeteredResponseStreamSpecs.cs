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

using BinStash.Server.Services.Usage;
using FluentAssertions;

namespace BinStash.Server.Tests.Billing;

/// <summary>
/// The egress meter's accounting. The invariant under test is that every byte written is reported
/// exactly once — across the checkpoints plus the final remainder — because each report is a
/// billable event and both double counting and dropping are revenue bugs.
/// </summary>
public class MeteredResponseStreamSpecs
{
    private static (MeteredResponseStream Stream, List<long> Checkpoints, MemoryStream Sink) Build(long checkpointBytes)
    {
        var checkpoints = new List<long>();
        var sink = new MemoryStream();
        return (new MeteredResponseStream(sink, checkpointBytes, checkpoints.Add), checkpoints, sink);
    }

    [Fact]
    public void ReportsACheckpointOnceTheThresholdIsCrossed()
    {
        var (stream, checkpoints, _) = Build(checkpointBytes: 100);

        stream.Write(new byte[60]);
        checkpoints.Should().BeEmpty("60 bytes has not reached the 100-byte checkpoint");

        stream.Write(new byte[60]);
        checkpoints.Should().Equal([120L], "the checkpoint reports everything accumulated, not just the threshold");
    }

    [Fact]
    public void ReportsRepeatedlyAsTheStreamProgresses()
    {
        var (stream, checkpoints, _) = Build(checkpointBytes: 10);

        for (var i = 0; i < 5; i++)
            stream.Write(new byte[10]);

        checkpoints.Should().Equal([10L, 10L, 10L, 10L, 10L], "a long download must be metered as it runs, so a disconnect loses at most one checkpoint");
    }

    [Fact]
    public void EveryByteIsReportedExactlyOnce()
    {
        var (stream, checkpoints, sink) = Build(checkpointBytes: 7);

        var sizes = new[] { 3, 11, 1, 40, 2, 19 };
        foreach (var size in sizes)
            stream.Write(new byte[size]);

        var total = checkpoints.Sum() + stream.TakeUnmeteredBytes();

        total.Should().Be(sizes.Sum());
        total.Should().Be(stream.BytesWritten);
        sink.Length.Should().Be(sizes.Sum(), "the bytes must still reach the underlying stream");
    }

    [Fact]
    public void TakeUnmeteredBytesClearsTheRemainder()
    {
        var (stream, _, _) = Build(checkpointBytes: 100);

        stream.Write(new byte[30]);

        stream.TakeUnmeteredBytes().Should().Be(30);
        stream.TakeUnmeteredBytes().Should().Be(0, "the remainder must not be billable twice — the finally block may run after a checkpoint already fired");
    }

    [Fact]
    public void ANonPositiveCheckpointDefersEverythingToTheEnd()
    {
        var (stream, checkpoints, _) = Build(checkpointBytes: 0);

        stream.Write(new byte[5_000]);
        stream.Write(new byte[5_000]);

        checkpoints.Should().BeEmpty();
        stream.TakeUnmeteredBytes().Should().Be(10_000);
    }

    [Fact]
    public async Task AsyncWritesAreCountedToo()
    {
        var (stream, checkpoints, _) = Build(checkpointBytes: 10);

        await stream.WriteAsync(new byte[25]);

        checkpoints.Should().Equal(25);
        stream.BytesWritten.Should().Be(25);
    }

    [Fact]
    public void SingleByteWritesAreCounted()
    {
        var (stream, checkpoints, _) = Build(checkpointBytes: 3);

        for (var i = 0; i < 7; i++)
            stream.WriteByte(1);

        checkpoints.Should().Equal(3, 3);
        stream.TakeUnmeteredBytes().Should().Be(1);
    }
}
