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
/// Only <em>logical</em> bytes are counted: the size of the content as the tenant handed it over,
/// before deduplication and compression. The stored footprint is a property of the shared chunk
/// store rather than of any one tenant — tenants deduplicate against each other, so a tenant's
/// physical share is not a well-defined number, and reporting one would leak what other tenants
/// hold. Logical bytes is also what the tenant is billed on, so quota and invoice agree.
///
/// <para>
/// This query was previously written out at each call site. It is the basis of the invoice and of
/// the quota decision, so the two must not be able to drift apart.
/// </para>
///
/// <para>
/// The join is spelled out in each method rather than factored into a shared <c>IQueryable</c>.
/// Projecting joined rows into a named intermediate type defeats the Npgsql translator — the
/// query cannot be translated at all and throws at runtime, which EF Core's in-memory provider
/// does not reproduce. Anonymous types all the way through is what translates, so the repetition
/// is load-bearing; the sharing that matters — one class owning the definition — is intact.
/// </para>
/// </remarks>
public sealed class TenantUsageService(BinStashDbContext db)
{
    /// <summary>Logical bytes currently held by one tenant across all of its repositories.</summary>
    public async Task<long> GetLogicalBytesAsync(Guid tenantId, CancellationToken ct = default)
        => (await GetTotalsAsync(tenantId, ct)).LogicalBytes;

    /// <summary>Logical bytes per tenant, for every tenant that holds any.</summary>
    public async Task<IReadOnlyList<TenantLogicalUsage>> GetLogicalBytesByTenantAsync(CancellationToken ct = default)
    {
        var rows = await db.ReleaseMetrics
            .AsNoTracking()
            .Join(db.Releases.AsNoTracking(), rm => rm.ReleaseId, r => r.Id, (rm, r) => new { rm.TotalLogicalBytes, r.RepoId })
            .Join(db.Repositories.AsNoTracking(), x => x.RepoId, repo => repo.Id, (x, repo) => new { x.TotalLogicalBytes, repo.TenantId })
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
            .Join(db.Releases.AsNoTracking(), rm => rm.ReleaseId, r => r.Id, (rm, r) => new { rm, r.RepoId })
            .Join(db.Repositories.AsNoTracking(), x => x.RepoId, repo => repo.Id, (x, repo) => new { x.rm, repo.TenantId })
            .Where(x => x.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(g => new { LogicalBytes = (long)g.Sum(x => (decimal)x.rm.TotalLogicalBytes), ReleaseCount = g.Count() })
            .FirstOrDefaultAsync(ct);

        var repositoryCount = await db.Repositories
            .AsNoTracking()
            .CountAsync(r => r.TenantId == tenantId, ct);

        return new TenantUsageTotals(totals?.LogicalBytes ?? 0, totals?.ReleaseCount ?? 0, repositoryCount);
    }
}

public sealed record TenantLogicalUsage(Guid TenantId, long LogicalBytes);

public sealed record TenantUsageTotals(long LogicalBytes, int ReleaseCount, int RepositoryCount);
