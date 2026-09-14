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

using System.Diagnostics;
using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Core.Storage;
using BinStash.Core.Storage.Gc;
using BinStash.Infrastructure.Data;
using BinStash.Server.Services.Reachability;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Services.Billing;

/// <summary>
/// Raised when a tenant's footprint cannot be established in full.
/// </summary>
/// <remarks>
/// Always preferred over returning a partial total. A footprint that silently omits part of a
/// tenant's content is not a smaller number, it is a wrong one, and it undercharges in a way
/// nobody would notice.
/// </remarks>
public sealed class TenantFootprintUnavailableException : Exception
{
    public TenantFootprintUnavailableException(string message) : base(message) { }
}

/// <summary>
/// Computes what one tenant's content measures, as if that tenant owned a private chunk store.
/// </summary>
/// <seealso cref="TenantStorageSnapshot"/>
public interface ITenantFootprintCalculator
{
    /// <exception cref="TenantFootprintUnavailableException">
    /// Some part of the tenant's content could not be walked. The caller should keep the previous
    /// snapshot rather than publish this one.
    /// </exception>
    Task<TenantStorageSnapshot> ComputeAsync(Guid tenantId, CancellationToken ct = default);
}

/// <inheritdoc cref="ITenantFootprintCalculator"/>
public sealed class TenantFootprintCalculator : ITenantFootprintCalculator
{
    /// <summary>
    /// How many hashes go into one <c>IN (...)</c> predicate. A large tenant reaches millions of
    /// chunks, and PostgreSQL's parameter limit is 65535.
    /// </summary>
    private const int LookupBatchSize = 2000;

    private readonly BinStashDbContext _db;
    private readonly IChunkStoreStorageFactory _storageFactory;
    private readonly ILogger<TenantFootprintCalculator> _logger;

    public TenantFootprintCalculator(BinStashDbContext db, IChunkStoreStorageFactory storageFactory, ILogger<TenantFootprintCalculator> logger)
    {
        _db = db;
        _storageFactory = storageFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TenantStorageSnapshot> ComputeAsync(Guid tenantId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var roots = await ReleaseRootQueries.ForTenant(_db, tenantId).ToListAsync(ct);
        var repositoryCount = await _db.Repositories.AsNoTracking().CountAsync(r => r.TenantId == tenantId, ct);

        if (roots.Count == 0)
        {
            sw.Stop();
            return new TenantStorageSnapshot
            {
                TenantId = tenantId,
                ComputedAt = DateTimeOffset.UtcNow,
                Duration = sw.Elapsed,
                RepositoryCount = repositoryCount
            };
        }

        var byStore = roots.GroupBy(r => r.ChunkStoreId).ToList();
        var stores = await ResolveStoresAsync(tenantId, byStore.Select(g => g.Key), ct);

        var spillRoot = System.IO.Path.Combine(
            stores.Values.First().Collector.WorkingDirectory,
            $"footprint-{tenantId:N}-{Guid.CreateVersion7():N}");

        // Deduplication spans the whole tenant, so chunks accumulate in one set across every
        // chunk store the tenant's repositories point at. Which store a repository was assigned
        // to is the operator's decision, and billing a tenant twice for one piece of their own
        // content because of it would be arbitrary.
        using var chunkMarks = new GcMarkSet(System.IO.Path.Combine(spillRoot, "chunks"));
        try
        {
            foreach (var group in byStore)
            {
                var (storage, collector) = stores[group.Key];
                var walker = new ReleaseReachabilityWalker(storage, collector);

                // File definitions are resolved against the store that holds them, so they are
                // marked per store. The same file definition reached through two stores is read
                // twice here and still contributes its chunks once, because chunkMarks is shared.
                using var fileMarks = new GcMarkSet(System.IO.Path.Combine(spillRoot, $"files-{group.Key:N}"));

                foreach (var scoped in group)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var fileHash in await WalkOrFailAsync(walker.ReadReferencedFileHashesAsync(scoped.Release, ct)))
                        fileMarks.Add(GcObjectCategory.FileDefinition, fileHash);
                }

                fileMarks.Flush();

                foreach (var prefix in fileMarks.MarkedPrefixes(GcObjectCategory.FileDefinition).ToList())
                {
                    ct.ThrowIfCancellationRequested();

                    foreach (var fileHash in fileMarks.LoadBucket(new GcBucketId(GcObjectCategory.FileDefinition, prefix)))
                    {
                        foreach (var chunkHash in await WalkOrFailAsync(walker.ReadChunkHashesAsync(fileHash, ct)))
                            chunkMarks.Add(GcObjectCategory.Chunk, chunkHash);
                    }

                    chunkMarks.Flush();
                }

                fileMarks.DeleteSpillFiles();
            }

            chunkMarks.Flush();

            var storeIds = byStore.Select(g => g.Key).ToList();
            var (uniqueBytes, uniqueChunks) = await SumUniqueChunkBytesAsync(chunkMarks, storeIds, tenantId, ct);
            var logicalBytes = await SumLogicalBytesAsync(tenantId, ct);

            sw.Stop();

            return new TenantStorageSnapshot
            {
                TenantId = tenantId,
                ComputedAt = DateTimeOffset.UtcNow,
                Duration = sw.Elapsed,
                UniqueLogicalBytes = uniqueBytes,
                UniqueChunkCount = uniqueChunks,
                LogicalBytes = logicalBytes,
                ReleaseCount = roots.Count,
                RepositoryCount = repositoryCount
            };
        }
        finally
        {
            chunkMarks.DeleteSpillFiles();
            try
            {
                if (System.IO.Directory.Exists(spillRoot))
                    System.IO.Directory.Delete(spillRoot, recursive: true);
            }
            catch
            {
                // Best effort, exactly as in GcMarkSet: leftover spill files cost disk, never
                // correctness.
            }
        }
    }

    /// <summary>
    /// Restates a reachability failure as a refusal to publish. A tenant walked only in part
    /// would be billed for less than they hold, which is worse than no snapshot at all.
    /// </summary>
    private static async Task<IReadOnlyList<Hash32>> WalkOrFailAsync(Task<IReadOnlyList<Hash32>> walk)
    {
        try
        {
            return await walk;
        }
        catch (ReachabilityAbortedException ex)
        {
            throw new TenantFootprintUnavailableException(
                $"{ex.Message} Refusing to publish a usage snapshot: the tenant would be billed for less " +
                "than they hold.");
        }
    }

    private async Task<Dictionary<Guid, (IChunkStoreStorage Storage, IChunkStoreGarbageCollector Collector)>> ResolveStoresAsync(Guid tenantId, IEnumerable<Guid> chunkStoreIds, CancellationToken ct)
    {
        var resolved = new Dictionary<Guid, (IChunkStoreStorage, IChunkStoreGarbageCollector)>();

        foreach (var id in chunkStoreIds)
        {
            var store = await _db.ChunkStores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (store is null)
                throw new TenantFootprintUnavailableException(
                    $"Tenant {tenantId} has releases in chunk store {id}, which no longer exists.");

            var storage = _storageFactory.Create(store);
            var collector = storage.GarbageCollector;

            // The walk resolves file definitions through the collector, the same reader garbage
            // collection marks with, so that both subsystems see one view of the store.
            if (collector is null)
                throw new TenantFootprintUnavailableException(
                    $"Chunk store '{store.Name}' ({store.Type}) cannot be walked, so the footprint of tenant " +
                    $"{tenantId} cannot be established.");

            resolved[id] = (storage, collector);
        }

        return resolved;
    }

    /// <summary>
    /// Uncompressed bytes of the distinct chunks the tenant reaches, counted once each.
    /// </summary>
    private async Task<(long Bytes, long Count)> SumUniqueChunkBytesAsync(GcMarkSet marks, IReadOnlyList<Guid> storeIds, Guid tenantId, CancellationToken ct)
    {
        long totalBytes = 0;
        long totalCount = 0;
        long missing = 0;

        foreach (var prefix in marks.MarkedPrefixes(GcObjectCategory.Chunk).ToList())
        {
            ct.ThrowIfCancellationRequested();

            var hashes = marks.LoadBucket(new GcBucketId(GcObjectCategory.Chunk, prefix));
            totalCount += hashes.Count;

            foreach (var batch in hashes.Chunk(LookupBatchSize))
            {
                var lookup = batch.ToList();

                // One row per distinct checksum: length is a property of the content, so a chunk
                // the tenant reaches through two of their chunk stores still counts once.
                var lengths = await _db.Chunks
                    .AsNoTracking()
                    .Where(c => storeIds.Contains(c.ChunkStoreId) && lookup.Contains(c.Checksum))
                    .GroupBy(c => c.Checksum)
                    .Select(g => new { Checksum = g.Key, Length = g.Max(c => c.Length) })
                    .ToListAsync(ct);

                totalBytes += lengths.Sum(x => (long)x.Length);
                missing += lookup.Count - lengths.Count;
            }
        }

        if (missing > 0)
        {
            // Reachable from a release but absent from the catalogue. The tenant holds content
            // whose size is unknown, so any total computed here is too low.
            throw new TenantFootprintUnavailableException(
                $"{missing:N0} chunks reachable from tenant {tenantId}'s releases have no catalogue row, so their " +
                "size is unknown. This usually means a concurrent garbage collection; the next run should settle it.");
        }

        return (totalBytes, totalCount);
    }

    /// <summary>
    /// What the tenant's releases would occupy extracted side by side. Informational: it is the
    /// figure the deduplication saving is measured against.
    /// </summary>
    private async Task<long> SumLogicalBytesAsync(Guid tenantId, CancellationToken ct)
    {
        var total = await _db.ReleaseMetrics
            .AsNoTracking()
            .Join(_db.Releases.AsNoTracking(), rm => rm.ReleaseId, r => r.Id, (rm, r) => new { rm, r.RepoId })
            .Join(_db.Repositories.AsNoTracking(), x => x.RepoId, repo => repo.Id, (x, repo) => new { x.rm, repo.TenantId })
            .Where(x => x.TenantId == tenantId)
            .SumAsync(x => (decimal)x.rm.TotalLogicalBytes, ct);

        return (long)total;
    }
}
