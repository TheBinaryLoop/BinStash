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
/// Every storage figure here is a function of this tenant's own releases and nothing else.
/// That is the rule, and it is what makes these numbers both billable and safe to show.
///
/// The billable one is <see cref="UniqueLogicalBytes"/>: the tenant's content deduplicated
/// against itself, as if they owned a private chunk store. Deduplicating within the tenant
/// is safe precisely because it consults no other tenant's data — and it is also the fair
/// measure, since it charges two tenants holding identical content identically, rather than
/// charging whoever uploaded a shared library first while everyone after them rides free.
///
/// What remains deliberately NOT exposed is the footprint after CROSS-tenant deduplication,
/// and any compression ratio of the shared store. Those move when other tenants upload: a
/// tenant watching them would learn that someone else stored identical content. They are
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
    /// Logical size of all releases as published, as if each were extracted side by side.
    /// Informational: it double-counts everything the tenant's releases share with each other.
    /// </summary>
    public long LogicalBytes { get; set; }

    /// <summary>
    /// The billable quantity: uncompressed bytes of the distinct content this tenant holds,
    /// counted once each.
    /// </summary>
    public long UniqueLogicalBytes { get; set; }

    /// <summary>
    /// <see cref="LogicalBytes"/> minus <see cref="UniqueLogicalBytes"/> — what the tenant is
    /// not being charged for because their own content repeats. Entirely their own data, so it
    /// is safe to show, and it is the figure that makes a multi-target release look like the
    /// bargain it is.
    /// </summary>
    public long DeduplicationSavedBytes { get; set; }

    /// <summary>
    /// When the footprint was last established by a full walk, or null if it never has been.
    /// Worth surfacing: the figure is a snapshot, and a tenant who just uploaded should be able
    /// to see that it predates their upload rather than conclude it was free.
    /// </summary>
    public DateTimeOffset? FootprintComputedAt { get; set; }

    public int RepositoryCount { get; set; }

    public int ReleaseCount { get; set; }

    /// <summary>Quota ceiling in unique logical bytes, or null when the plan imposes none.</summary>
    public long? MaxStorageBytes { get; set; }

    /// <summary>False when no finite storage quota applies (self-hosted / NoOp billing).</summary>
    public bool IsLimited { get; set; }

    /// <summary>Fraction of the storage quota consumed (0..n), or null when unlimited.</summary>
    public double? StorageUsedFraction { get; set; }

    public bool IsStorageAllowed { get; set; }

    public bool IsIngestAllowed { get; set; }

    public bool IsEgressAllowed { get; set; }
}
