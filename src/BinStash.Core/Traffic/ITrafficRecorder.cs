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

namespace BinStash.Core.Traffic;

/// <summary>
/// Accumulates per-tenant wire traffic. Called from request hot paths, so implementations must be
/// cheap, thread-safe, and must never throw into the caller — a lost traffic sample is a gap in a
/// chart, while a thrown one is a failed upload.
/// </summary>
public interface ITrafficRecorder
{
    /// <summary>Bytes accepted from a client.</summary>
    void RecordIngress(Guid tenantId, long bytes);

    /// <summary>Bytes served to a client.</summary>
    void RecordEgress(Guid tenantId, long bytes);
}

/// <summary>
/// One bucket's worth of accumulated traffic, on its way to storage.
/// </summary>
public readonly record struct TrafficDelta(Guid TenantId, DateTimeOffset BucketStartUtc, long IngressBytes, long EgressBytes, long RequestCount);

/// <summary>
/// Persistence for accumulated traffic, plus the maintenance that keeps the series bounded.
/// </summary>
/// <remarks>
/// Separate from <see cref="ITrafficRecorder"/> because the two have opposite constraints: the
/// recorder runs on the hot path and must not touch a database, while this runs on a timer and
/// does nothing else.
/// </remarks>
public interface ITrafficStore
{
    /// <summary>
    /// Adds the deltas to their buckets, creating buckets that do not exist yet.
    /// </summary>
    /// <remarks>
    /// Must be additive rather than last-write-wins: several replicas flush into the same bucket,
    /// and a read-modify-write would silently drop whichever flush lost the race.
    /// </remarks>
    Task AccumulateAsync(IReadOnlyCollection<TrafficDelta> deltas, CancellationToken ct = default);

    /// <summary>
    /// Folds hourly buckets that start before <paramref name="cutoffUtc"/> into daily buckets and
    /// removes the hourly rows. Idempotent: re-running it over an already-folded range adds
    /// nothing, because the hourly rows it would read are gone.
    /// </summary>
    Task RollUpHourlyAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default);

    /// <summary>Deletes daily buckets that start before <paramref name="cutoffUtc"/>.</summary>
    Task PruneDailyAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default);
}
