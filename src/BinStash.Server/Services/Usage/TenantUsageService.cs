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

namespace BinStash.Server.Services.Usage;

/// <summary>
/// The single definition of how much storage a tenant is using.
/// </summary>
/// <remarks>
/// The billable figure is the tenant's own deduplicated footprint: their content deduplicated
/// against <em>itself</em>, as if they owned a private chunk store. Deduplicating within the tenant
/// is safe precisely because it consults no other tenant's data, and it is the fair measure — two
/// tenants holding identical content are billed identically, rather than whoever uploaded a shared
/// library first paying for everyone after them.
///
/// <para>
/// What is still never counted or reported is the footprint after deduplication ACROSS tenants.
/// Tenants deduplicate against each other, so a tenant's physical share is not a well-defined
/// number, and reporting one would leak what other tenants hold.
/// </para>
///
/// <para>
/// Logical bytes — the size of the content as the tenant handed it over — remain available as the
/// figure the saving is measured against, but nothing is billed or gated on them.
/// </para>
///
/// <para>
/// This query was previously written out at each call site. It is the basis of the invoice and of
/// the quota decision, so the two must not be able to drift apart.
/// </para>
///
/// <para>
/// Metrics now hang off the release variant, so these reach the tenant by navigation
/// (<c>m.Variant.Release.Repository.TenantId</c>) rather than by explicit joins. That sidesteps
/// the trap the joins had: projecting joined rows into a named intermediate type defeats the
/// Npgsql translator, and the query then throws at runtime in a way EF Core's in-memory provider
/// does not reproduce. Navigation plus anonymous types translates, and was verified against a
/// real PostgreSQL rather than only against the in-memory provider.
/// </para>
/// </remarks>
public sealed class TenantUsageService(BinStashDbContext db)
{
    /// <summary>Logical bytes currently held by one tenant across all of its repositories.</summary>
    /// <remarks>Informational. Bill and gate on <see cref="GetBillableBytesAsync"/>.</remarks>
    public async Task<long> GetLogicalBytesAsync(Guid tenantId, CancellationToken ct = default)
        => (await GetTotalsAsync(tenantId, ct)).LogicalBytes;

    /// <summary>What one tenant is charged for, and what their quota is measured against.</summary>
    /// <remarks>
    /// Read from the newest footprint snapshot rather than computed here: establishing it costs a
    /// full reachability walk, which cannot happen inside a page load or an admission check.
    ///
    /// <para>
    /// Until the first walk completes there is no snapshot, and logical bytes stand in. They can
    /// only overstate what the tenant holds, so a new tenant is never quietly let past a quota
    /// they have already exceeded.
    /// </para>
    /// </remarks>
    public async Task<long> GetBillableBytesAsync(Guid tenantId, CancellationToken ct = default)
        => (await GetTotalsAsync(tenantId, ct)).BillableBytes;

    /// <summary>Logical bytes per tenant, for every tenant that holds any.</summary>
    public async Task<IReadOnlyList<TenantLogicalUsage>> GetLogicalBytesByTenantAsync(CancellationToken ct = default)
    {
        var rows = await db.ReleaseMetrics
            .AsNoTracking()
            .Select(m => new { m.TotalLogicalBytes, m.Variant.Release.Repository.TenantId })
            .GroupBy(x => x.TenantId)
            .Select(g => new { TenantId = g.Key, TotalBytes = (long)g.Sum(x => (decimal)x.TotalLogicalBytes) })
            .ToListAsync(ct);

        return rows.Select(r => new TenantLogicalUsage(r.TenantId, r.TotalBytes)).ToList();
    }

    /// <summary>Logical bytes plus the counts the usage page reports alongside them.</summary>
    public async Task<TenantUsageTotals> GetTotalsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var totals = await db.ReleaseMetrics
            .AsNoTracking()
            .Where(m => m.Variant.Release.Repository.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(g => new { LogicalBytes = (long)g.Sum(x => (decimal)x.TotalLogicalBytes) })
            .FirstOrDefaultAsync(ct);

        // Releases, not variants: a version published for five platforms is one release. Counting
        // the metrics rows would count it five times.
        var releaseCount = await db.Releases
            .AsNoTracking()
            .CountAsync(r => r.Repository.TenantId == tenantId, ct);

        var repositoryCount = await db.Repositories
            .AsNoTracking()
            .CountAsync(r => r.TenantId == tenantId, ct);

        var snapshot = await db.TenantStorageSnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.ComputedAt)
            .Select(s => new { s.UniqueLogicalBytes, s.ComputedAt })
            .FirstOrDefaultAsync(ct);

        var logicalBytes = totals?.LogicalBytes ?? 0;

        return new TenantUsageTotals(
            logicalBytes,
            snapshot?.UniqueLogicalBytes ?? logicalBytes,
            snapshot?.ComputedAt,
            releaseCount,
            repositoryCount);
    }
}

public sealed record TenantLogicalUsage(Guid TenantId, long LogicalBytes);

/// <param name="LogicalBytes">What the tenant's releases would occupy extracted side by side.</param>
/// <param name="BillableBytes">The deduplicated footprint — what they are actually charged for.</param>
/// <param name="FootprintComputedAt">When the last walk established it, or null if none has.</param>
public sealed record TenantUsageTotals(long LogicalBytes, long BillableBytes, DateTimeOffset? FootprintComputedAt, int ReleaseCount, int RepositoryCount);
