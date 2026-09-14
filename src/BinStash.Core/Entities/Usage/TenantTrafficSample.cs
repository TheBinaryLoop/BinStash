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

namespace BinStash.Core.Entities;

/// <summary>
/// Bytes a tenant moved in and out of the instance during one time bucket.
/// </summary>
/// <remarks>
/// Deliberately part of the open-source core rather than of the commercial billing plugin.
/// Knowing how much traffic your own instance is moving is operational visibility, not billing —
/// an AGPL deployment with no billing provider still needs to answer "why did the link saturate on
/// Tuesday". The billing meter (<c>IUsageMeteringService</c>) stays a separate concern and a
/// separate call; this table is never read to produce an invoice.
///
/// <para>
/// Counted in wire bytes at the same points the meter fires: chunk payloads accepted on the ingest
/// path, and bytes written to the response on the download path. That makes it a measure of what
/// crossed the network, not of what the content logically weighs — which is the number an operator
/// cares about, and is deliberately unlike the logical bytes that storage quota uses.
/// </para>
///
/// <para>
/// Aggregated in memory and flushed periodically, so a hard process kill can lose up to one flush
/// interval. That is an accepted trade: recording every chunk upload synchronously would put a
/// database write on the hot path of the ingest pipeline.
/// </para>
/// </remarks>
public class TenantTrafficSample
{
    public Guid TenantId { get; set; }

    /// <summary>Start of the bucket, always UTC and always truncated to <see cref="Grain"/>.</summary>
    public DateTimeOffset BucketStartUtc { get; set; }

    public TrafficGrain Grain { get; set; }

    /// <summary>Bytes accepted from clients — chunk and file-definition payloads.</summary>
    public long IngressBytes { get; set; }

    /// <summary>Bytes served to clients, as written to the response.</summary>
    public long EgressBytes { get; set; }

    /// <summary>How many recorded operations fell into this bucket. Useful for spotting a change in shape rather than volume.</summary>
    public long RequestCount { get; set; }
}

/// <summary>
/// The resolution of a <see cref="TenantTrafficSample"/>.
/// </summary>
/// <remarks>
/// Two grains rather than one so history stays both useful and bounded: hourly is fine-grained
/// enough to show a CI burst, but keeping it forever would be roughly 720 rows per tenant per
/// month. Hourly buckets are rolled up into daily ones once they age out, and the daily series is
/// what supports a year-over-year view.
/// </remarks>
public enum TrafficGrain
{
    Hourly = 0,
    Daily = 1
}
