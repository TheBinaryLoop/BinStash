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

namespace BinStash.Server.Configuration;

/// <summary>
/// Per-tenant traffic recording. Bind to the <c>Traffic</c> section.
/// </summary>
public sealed class TrafficSettings
{
    public const string SectionName = "Traffic";

    /// <summary>
    /// Whether traffic is recorded at all. Defaults to <c>true</c>; turning it off stops the
    /// writes and leaves the charts empty, it does not affect ingest, download or billing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often buffered traffic is written out.
    /// </summary>
    /// <remarks>
    /// Also the loss window: a hard process kill discards whatever has not been flushed. Shorter
    /// is more durable and more write traffic — each flush is one statement per active tenant.
    /// </remarks>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long hourly buckets are kept before being folded into daily ones.
    /// </summary>
    /// <remarks>
    /// Hourly resolution is what makes a CI burst visible, and 30 days of it is roughly 720 rows
    /// per tenant. Past that the question being asked changes from "what happened on Tuesday
    /// afternoon" to "how is this trending", which daily answers just as well for a thirtieth of
    /// the rows.
    /// </remarks>
    public TimeSpan HourlyRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long daily buckets are kept. 13 months so a year-over-year comparison has both ends.</summary>
    public TimeSpan DailyRetention { get; set; } = TimeSpan.FromDays(396);

    /// <summary>How often the fold-and-prune maintenance runs.</summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromHours(6);
}
