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

namespace BinStash.Server.GraphQL.Features.Usage;

/// <summary>
/// Consumption and entitlement for a single tenant.
/// </summary>
/// <remarks>
/// Reports ONLY undeduplicated, uncompressed logical bytes, because that is what a tenant
/// is billed on and it is the only storage figure that is purely a function of the
/// tenant's own data.
///
/// Deduplicated/compressed footprint and the savings derived from them are deliberately
/// NOT exposed here. Tenants share a chunk store, so those numbers depend on what other
/// tenants have uploaded: a tenant watching its own "bytes saved" move after an upload
/// learns that someone else already stored identical content. Those figures are still
/// meaningful for the instance as a whole and belong on instance-admin surfaces.
///
/// Limits come from <see cref="Core.Billing.IBillingProvider"/>, which resolves to the NoOp
/// implementation in the AGPL build. That deliberately reports "unlimited", so clients
/// should branch on <see cref="IsLimited"/> rather than assuming a finite quota.
/// </remarks>
public sealed class TenantUsageGql
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Logical size of all releases as published, before deduplication or compression.
    /// The billable quantity.
    /// </summary>
    public long LogicalBytes { get; set; }

    public int RepositoryCount { get; set; }

    public int ReleaseCount { get; set; }

    /// <summary>Quota ceiling in logical bytes, or null when the plan imposes none.</summary>
    public long? MaxStorageBytes { get; set; }

    /// <summary>False when no finite storage quota applies (self-hosted / NoOp billing).</summary>
    public bool IsLimited { get; set; }

    /// <summary>Fraction of the storage quota consumed (0..n), or null when unlimited.</summary>
    public double? StorageUsedFraction { get; set; }

    public bool IsStorageAllowed { get; set; }

    public bool IsIngestAllowed { get; set; }

    public bool IsEgressAllowed { get; set; }
}
