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

using System.Collections.Concurrent;

namespace BinStash.Core.Traffic;

/// <summary>
/// Accumulates traffic in memory, keyed by tenant and hour, until something drains it.
/// </summary>
/// <remarks>
/// A singleton on the ingest hot path: <c>UploadChunks</c> calls this once per chunk message, so
/// it does interlocked adds into a dictionary and nothing else. Persisting each call would put a
/// database round trip between every chunk of every upload.
///
/// <para>
/// The cost of that choice is a bounded loss window — whatever has not been drained when the
/// process dies is gone. For a traffic chart that is the right trade; for the billing meter it
/// would not be, which is why the billing meter is a separate call that a plugin can make durable
/// on its own terms.
/// </para>
/// </remarks>
public sealed class BufferedTrafficRecorder : ITrafficRecorder
{
    /// <summary>
    /// How far past its hour a bucket must be before an idle drain removes it.
    /// </summary>
    /// <remarks>
    /// Removal is the one operation that can actually lose a sample: a writer that has already
    /// taken a reference to a bucket, but has not yet added to it, would add to a bucket no longer
    /// in the map. Writers only ever touch the current hour, so waiting a couple of hours before
    /// reclaiming makes that window implausible rather than merely unlikely.
    /// </remarks>
    private static readonly TimeSpan ReclaimAfter = TimeSpan.FromHours(2);

    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TrafficBucketKey, Bucket> _buckets = new();

    public BufferedTrafficRecorder(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void RecordIngress(Guid tenantId, long bytes) => Add(tenantId, bytes, 0);

    public void RecordEgress(Guid tenantId, long bytes) => Add(tenantId, 0, bytes);

    private void Add(Guid tenantId, long ingress, long egress)
    {
        // A zero-byte operation still happened; counting it keeps RequestCount honest. A negative
        // one cannot have, and would corrupt the series, so it is dropped rather than trusted.
        if (tenantId == Guid.Empty || ingress < 0 || egress < 0)
            return;

        var key = new TrafficBucketKey(tenantId, TruncateToHour(_timeProvider.GetUtcNow()));
        var bucket = _buckets.GetOrAdd(key, static _ => new Bucket());

        bucket.Add(ingress, egress);
    }

    /// <summary>
    /// Takes everything accumulated so far, leaving the buckets in place and zeroed.
    /// </summary>
    /// <remarks>
    /// The counters are reset with interlocked exchanges rather than the map being swapped or
    /// emptied. Swapping looks tidier but loses samples: a writer resolves its bucket reference
    /// and then adds to it, and if the swap happens between those two steps the add lands in a
    /// bucket the drain has already read and discarded. Because a bucket object here outlives any
    /// number of drains, an add is always either included in the current take or left for the
    /// next one — never dropped.
    /// </remarks>
    public IReadOnlyCollection<TrafficDelta> Drain()
    {
        if (_buckets.IsEmpty)
            return [];

        var reclaimBefore = TruncateToHour(_timeProvider.GetUtcNow()) - ReclaimAfter;
        var deltas = new List<TrafficDelta>();

        foreach (var (key, bucket) in _buckets)
        {
            var (ingress, egress, count) = bucket.Take();

            if (ingress == 0 && egress == 0 && count == 0)
            {
                // Idle and old enough that no writer can still be targeting it.
                if (key.BucketStartUtc < reclaimBefore)
                    _buckets.TryRemove(key, out _);

                continue;
            }

            deltas.Add(new TrafficDelta(key.TenantId, key.BucketStartUtc, ingress, egress, count));
        }

        return deltas;
    }

    public static DateTimeOffset TruncateToHour(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private readonly record struct TrafficBucketKey(Guid TenantId, DateTimeOffset BucketStartUtc);

    private sealed class Bucket
    {
        private long _ingress;
        private long _egress;
        private long _count;

        public void Add(long ingress, long egress)
        {
            if (ingress != 0) Interlocked.Add(ref _ingress, ingress);
            if (egress != 0) Interlocked.Add(ref _egress, egress);
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// Reads and zeroes the counters. Each exchange is independent, so a concurrent add may
        /// land partly in this take and partly in the next — the totals stay exact either way,
        /// which is what the series needs.
        /// </summary>
        public (long Ingress, long Egress, long Count) Take()
            => (Interlocked.Exchange(ref _ingress, 0), Interlocked.Exchange(ref _egress, 0), Interlocked.Exchange(ref _count, 0));
    }
}
