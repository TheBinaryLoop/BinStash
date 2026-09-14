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

using BinStash.Core.Traffic;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace BinStash.Core.Tests.Traffic;

/// <summary>
/// The recorder sits on the ingest hot path and is drained concurrently with being written to,
/// so the properties worth pinning are that nothing is double counted, nothing is lost across a
/// drain, and buckets are attributed to the right hour.
/// </summary>
public class BufferedTrafficRecorderSpecs
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 30, 0, TimeSpan.Zero);

    private static (BufferedTrafficRecorder Recorder, FakeTimeProvider Time) Build()
    {
        var time = new FakeTimeProvider(Noon);
        return (new BufferedTrafficRecorder(time), time);
    }

    [Fact]
    public void AccumulatesIngressAndEgressIntoOneBucket()
    {
        var (recorder, _) = Build();
        var tenant = Guid.NewGuid();

        recorder.RecordIngress(tenant, 100);
        recorder.RecordIngress(tenant, 50);
        recorder.RecordEgress(tenant, 25);

        var delta = recorder.Drain().Should().ContainSingle().Subject;
        delta.TenantId.Should().Be(tenant);
        delta.IngressBytes.Should().Be(150);
        delta.EgressBytes.Should().Be(25);
        delta.RequestCount.Should().Be(3);
    }

    [Fact]
    public void TruncatesTheBucketToTheHour()
    {
        var (recorder, _) = Build();
        recorder.RecordIngress(Guid.NewGuid(), 1);

        var delta = recorder.Drain().Single();

        delta.BucketStartUtc.Should().Be(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void SeparatesBucketsWhenTheHourRolls()
    {
        var (recorder, time) = Build();
        var tenant = Guid.NewGuid();

        recorder.RecordIngress(tenant, 10);
        time.Advance(TimeSpan.FromHours(1));
        recorder.RecordIngress(tenant, 20);

        var deltas = recorder.Drain().OrderBy(d => d.BucketStartUtc).ToList();

        deltas.Should().HaveCount(2);
        deltas[0].IngressBytes.Should().Be(10);
        deltas[1].IngressBytes.Should().Be(20);
    }

    [Fact]
    public void SeparatesTenants()
    {
        var (recorder, _) = Build();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        recorder.RecordIngress(a, 10);
        recorder.RecordIngress(b, 20);

        var deltas = recorder.Drain();

        deltas.Should().HaveCount(2);
        deltas.Single(d => d.TenantId == a).IngressBytes.Should().Be(10);
        deltas.Single(d => d.TenantId == b).IngressBytes.Should().Be(20);
    }

    [Fact]
    public void DrainEmptiesTheBuffer()
    {
        var (recorder, _) = Build();
        recorder.RecordIngress(Guid.NewGuid(), 10);

        recorder.Drain().Should().ContainSingle();
        recorder.Drain().Should().BeEmpty("a flushed sample must not be written a second time");
    }

    [Fact]
    public void IgnoresSamplesItCannotAttributeOrTrust()
    {
        var (recorder, _) = Build();

        recorder.RecordIngress(Guid.Empty, 10);
        recorder.RecordIngress(Guid.NewGuid(), -5);
        recorder.RecordEgress(Guid.NewGuid(), -1);

        recorder.Drain().Should().BeEmpty();
    }

    [Fact]
    public void CountsAZeroByteOperation()
    {
        var (recorder, _) = Build();
        recorder.RecordEgress(Guid.NewGuid(), 0);

        var delta = recorder.Drain().Should().ContainSingle().Subject;
        delta.RequestCount.Should().Be(1, "the operation happened even though it moved nothing");
        delta.EgressBytes.Should().Be(0);
    }

    [Fact]
    public async Task LosesNothingWhenWrittenToAndDrainedConcurrently()
    {
        // The drain swaps the whole map rather than emptying it in place; this is the property
        // that choice exists to protect.
        var (recorder, _) = Build();
        var tenant = Guid.NewGuid();
        const int writers = 8;
        const int perWriter = 2_000;

        var drained = new List<TrafficDelta>();
        using var stop = new CancellationTokenSource();

        var drainLoop = Task.Run(async () =>
        {
            while (!stop.Token.IsCancellationRequested)
            {
                drained.AddRange(recorder.Drain());
                await Task.Yield();
            }
        });

        await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
                recorder.RecordIngress(tenant, 1);
        })));

        await stop.CancelAsync();
        await drainLoop;
        drained.AddRange(recorder.Drain());

        drained.Sum(d => d.IngressBytes).Should().Be(writers * perWriter);
        drained.Sum(d => d.RequestCount).Should().Be(writers * perWriter);
    }
}
