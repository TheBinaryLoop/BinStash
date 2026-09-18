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

using BinStash.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Services.Reachability;

/// <summary>
/// A release root together with the chunk store its objects live in.
/// </summary>
public readonly record struct ScopedReleaseRoot(Guid ChunkStoreId, ReleaseRoot Release);

/// <summary>
/// The one place that answers "which release definitions root a reachability walk".
///
/// <para>
/// Garbage collection asks per chunk store; usage accounting asks per tenant. Both must see the
/// same set, and a release definition that either query forgets is a release definition whose
/// chunks one subsystem collects and the other bills for. Keeping both projections here means a
/// change to how releases carry their definitions is made once — as when definitions moved from
/// the release to its per-target variants, which is a change entirely contained by this file.
/// </para>
/// </summary>
public static class ReleaseRootQueries
{
    /// <summary>
    /// Every release definition stored in the given chunk store, across all tenants.
    /// </summary>
    public static IQueryable<ReleaseRoot> ForChunkStore(BinStashDbContext db, Guid chunkStoreId) =>
        db.ReleaseVariants
            .AsNoTracking()
            .Where(v => v.Release.Repository.ChunkStoreId == chunkStoreId)
            .Select(v => new ReleaseRoot(v.Id, v.Release.Version, v.TargetKey, v.ReleaseDefinitionChecksum));

    /// <summary>
    /// Every release definition owned by the given tenant, tagged with the chunk store holding it.
    /// </summary>
    /// <remarks>
    /// A tenant's repositories may point at different chunk stores, so the caller has to resolve
    /// storage per chunk store rather than once per tenant.
    /// </remarks>
    public static IQueryable<ScopedReleaseRoot> ForTenant(BinStashDbContext db, Guid tenantId) =>
        db.ReleaseVariants
            .AsNoTracking()
            .Where(v => v.Release.Repository.TenantId == tenantId)
            .Select(v => new ScopedReleaseRoot(
                v.Release.Repository.ChunkStoreId,
                new ReleaseRoot(v.Id, v.Release.Version, v.TargetKey, v.ReleaseDefinitionChecksum)));
}
