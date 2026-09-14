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
/// How the host drives the billing boundary. Bind to the <c>Billing</c> section.
/// </summary>
/// <remarks>
/// Nothing here describes a plan or a price — that is the billing provider's business, and under
/// the AGPL default there is none. These are the mechanics of <em>when</em> the host tells the
/// provider something.
/// </remarks>
public sealed class BillingSettings
{
    public const string SectionName = "Billing";

    /// <summary>How often per-tenant storage totals are reported to the meter, in minutes.</summary>
    public int StorageStatsIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// How many bytes a download may serve between egress meter reports.
    /// </summary>
    /// <remarks>
    /// This is the bound on how much egress a crash or a client disconnect can lose: the bytes
    /// served since the last checkpoint. Smaller is more accurate and more chatty — each
    /// checkpoint is a call into the billing provider, which for a metered plugin is likely a
    /// write. 64 MiB keeps a multi-gigabyte download to a couple of dozen reports while bounding
    /// the loss at something small next to the monthly bill.
    ///
    /// <para>Set to 0 to report only once, when the download ends.</para>
    /// </remarks>
    public long EgressMeterCheckpointBytes { get; set; } = 64L * 1024 * 1024;
}
