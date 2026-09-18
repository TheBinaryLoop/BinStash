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
using GreenDonut;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.Repositories;

/// <summary>
/// Loads per-repository totals for a whole page of repositories in one query.
/// </summary>
/// <remarks>
/// Batched rather than resolved per repository because this field exists to be rendered in a
/// <em>list</em> — the repositories page asks for it once per row, and a naive resolver would
/// turn one page load into one aggregate query per repository.
///
/// <para>
/// The sum is taken in memory rather than in SQL: <c>ReleaseMetrics.TotalLogicalBytes</c> is a
/// <see cref="ulong"/> mapped to <c>numeric</c>, and the narrowing back to a signed 64-bit total
/// has no translation. Grouping still happens in the database, so what is materialised is one row
/// per repository, not one per release.
/// </para>
/// </remarks>
public sealed class RepositoryMetricsDataLoader : BatchDataLoader<Guid, RepositoryMetricsGql>
{
    private readonly BinStashDbContext _db;

    public RepositoryMetricsDataLoader(
        BinStashDbContext db,
        IBatchScheduler batchScheduler,
        DataLoaderOptions options)
        : base(batchScheduler, options)
    {
        _db = db;
    }

    protected override async Task<IReadOnlyDictionary<Guid, RepositoryMetricsGql>> LoadBatchAsync(
        IReadOnlyList<Guid> keys, CancellationToken cancellationToken)
    {
        // Two straightforward aggregates rather than one join-and-group: counting releases needs
        // no metrics row (older releases predate metric collection and must still be counted),
        // and summing bytes needs the unsigned-to-decimal cast that the billing snapshot uses.
        var counts = await _db.Releases
            .AsNoTracking()
            .Where(r => keys.Contains(r.RepoId))
            .GroupBy(r => r.RepoId)
            .Select(g => new
            {
                RepoId = g.Key,
                ReleaseCount = g.Count(),
                LastReleaseAt = g.Max(r => r.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        var sizes = await _db.ReleaseMetrics
            .AsNoTracking()
            .Where(m => keys.Contains(m.Variant.Release.RepoId))
            .Select(m => new { m.Variant.Release.RepoId, m.TotalLogicalBytes })
            .GroupBy(x => x.RepoId)
            .Select(g => new
            {
                RepoId = g.Key,
                TotalLogicalBytes = g.Sum(x => (decimal)x.TotalLogicalBytes)
            })
            .ToDictionaryAsync(x => x.RepoId, x => x.TotalLogicalBytes, cancellationToken);

        var byRepo = counts.ToDictionary(
            x => x.RepoId,
            x => new RepositoryMetricsGql
            {
                ReleaseCount = x.ReleaseCount,
                TotalLogicalBytes = sizes.TryGetValue(x.RepoId, out var bytes) ? (long)bytes : 0,
                LastReleaseAt = x.LastReleaseAt
            });

        // A repository with no releases produces no group, and a DataLoader key with no value
        // resolves to null. An empty repository has a known size — zero — so it is filled in
        // rather than left as "unknown".
        foreach (var key in keys)
            byRepo.TryAdd(key, RepositoryMetricsGql.Empty);

        return byRepo;
    }
}
