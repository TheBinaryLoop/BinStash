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

namespace BinStash.Server.GraphQL.Features.Traffic;

/// <summary>
/// Bytes moved in and out during one bucket.
/// </summary>
/// <remarks>
/// Wire bytes, not logical ones: chunk payloads accepted and response bytes served. That makes
/// this a different quantity from the storage figures on the usage page, which count content at
/// its logical size — a release that deduplicates almost entirely shows large storage and small
/// ingress, and that difference is the point.
/// </remarks>
public sealed class TrafficPointGql
{
    /// <summary>Start of the bucket, UTC.</summary>
    public DateTimeOffset BucketStartUtc { get; set; }

    public long IngressBytes { get; set; }

    public long EgressBytes { get; set; }

    public long RequestCount { get; set; }
}

/// <summary>A traffic series plus its totals, so a caller does not have to re-sum what it just fetched.</summary>
public sealed class TrafficSeriesGql
{
    public TrafficGrainGql Grain { get; set; }

    public DateTimeOffset FromUtc { get; set; }

    public DateTimeOffset ToUtc { get; set; }

    /// <summary>
    /// One entry per bucket that has data. Buckets with no traffic are absent rather than zero —
    /// a chart that wants a continuous axis fills the gaps, and a table that does not want them
    /// is spared the noise.
    /// </summary>
    public List<TrafficPointGql> Points { get; set; } = [];

    public long TotalIngressBytes { get; set; }

    public long TotalEgressBytes { get; set; }

    public long TotalRequestCount { get; set; }
}

/// <summary>
/// Instance-wide traffic: the whole instance's series, plus a per-tenant split.
/// </summary>
/// <remarks>
/// Only reachable by an instance admin. The per-tenant breakdown names tenants and their volumes,
/// which is exactly the cross-tenant information the tenant-facing surfaces withhold.
/// </remarks>
public sealed class InstanceTrafficGql
{
    public TrafficSeriesGql Total { get; set; } = new();

    public List<TenantTrafficTotalGql> ByTenant { get; set; } = [];
}

public sealed class TenantTrafficTotalGql
{
    public Guid TenantId { get; set; }

    public string TenantName { get; set; } = string.Empty;

    public long IngressBytes { get; set; }

    public long EgressBytes { get; set; }

    public long RequestCount { get; set; }
}

public enum TrafficGrainGql
{
    Hourly = 0,
    Daily = 1
}
