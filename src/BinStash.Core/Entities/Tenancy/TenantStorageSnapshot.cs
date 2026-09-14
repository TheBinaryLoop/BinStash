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
/// What one tenant's stored content measures, computed as if that tenant owned a private chunk
/// store.
///
/// <para>
/// Every figure here is a function of the tenant's own releases and nothing else. That is not an
/// implementation detail — it is the property that makes the number both billable and safe to
/// show. Because chunk stores are shared and deduplicated across tenants, any measure that
/// consulted the store's actual contents would move when an unrelated tenant uploaded or deleted
/// something: it would leak their activity, and it would charge whoever happened to upload a
/// shared library first while everyone after them rode free. Measuring the tenant in isolation
/// removes both problems at once, and makes two tenants holding identical content pay identically
/// regardless of who arrived first.
/// </para>
///
/// <para>
/// The provider keeps the cross-tenant deduplication and compression savings as margin. They are
/// real, but they cannot be attributed to any one tenant, so they are not charged to one either.
/// </para>
/// </summary>
public class TenantStorageSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }
    public virtual Tenant Tenant { get; set; } = null!;

    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>
    /// How long the reachability walk behind this snapshot took. Kept because the walk is the
    /// expensive part of billing and its growth is the thing that decides when the cadence has to
    /// change.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// The billable figure: uncompressed bytes of the distinct chunks this tenant's releases
    /// reach, counted once each.
    /// </summary>
    /// <remarks>
    /// Uncompressed rather than stored bytes on purpose. Compressed size would also be leak-free,
    /// but it would move whenever compression settings were retuned and it would expose how well
    /// the operator's storage performs. Uncompressed is stable, and a customer can verify it
    /// against their own data.
    /// </remarks>
    public long UniqueLogicalBytes { get; set; }

    /// <summary>
    /// What the tenant's releases would occupy if every one of them were extracted side by side.
    /// Informational only; it double-counts everything shared between releases and between the
    /// variants of a release.
    /// </summary>
    public long LogicalBytes { get; set; }

    /// <summary>
    /// Distinct chunks reached. Diagnostic, and the cheapest signal that a walk did far less or
    /// far more work than the previous one.
    /// </summary>
    public long UniqueChunkCount { get; set; }

    public int ReleaseCount { get; set; }

    public int RepositoryCount { get; set; }
}
