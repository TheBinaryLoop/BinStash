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
/// Limits come from <see cref="Core.Billing.IBillingProvider"/>, which resolves to the NoOp
/// implementation in the AGPL build. That deliberately reports "unlimited", so this type stays
/// meaningful on a self-hosted instance with no billing plugin loaded — clients should branch on
/// <see cref="IsLimited"/> rather than assuming a finite quota.
/// </remarks>
public sealed class TenantUsageGql
{
    public Guid TenantId { get; set; }

    /// <summary>Logical size of all releases as users see them, before dedup/compression.</summary>
    public long LogicalBytes { get; set; }

    /// <summary>Unique uncompressed bytes actually retained after deduplication.</summary>
    public long StoredBytes { get; set; }

    /// <summary>Unique compressed bytes on disk. The number a storage bill is based on.</summary>
    public long CompressedBytes { get; set; }

    public long DeduplicationSavedBytes { get; set; }

    public long CompressionSavedBytes { get; set; }

    public int RepositoryCount { get; set; }

    public int ReleaseCount { get; set; }

    /// <summary>Quota ceiling, or null when the active plan does not impose one.</summary>
    public long? MaxStorageBytes { get; set; }

    /// <summary>False when no finite storage quota applies (self-hosted / NoOp billing).</summary>
    public bool IsLimited { get; set; }

    /// <summary>Fraction of the storage quota consumed (0..n), or null when unlimited.</summary>
    public double? StorageUsedFraction { get; set; }

    public bool IsStorageAllowed { get; set; }

    public bool IsIngestAllowed { get; set; }

    public bool IsEgressAllowed { get; set; }
}
