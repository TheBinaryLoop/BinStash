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

using System.Runtime.CompilerServices;
using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Core.Storage.Gc;

namespace BinStash.Infrastructure.Storage.Gc;

/// <summary>
/// <see cref="IChunkStoreGarbageCollector"/> over a local pack store.
///
/// <para>
/// This type is deliberately thin. All of the concurrency-sensitive work lives in
/// <see cref="Indexing.IndexedPackFileHandler"/>, where the write lock, the append log and
/// the segment list are; this class only maps buckets onto handlers and keeps the leases
/// short. Holding a handler lease across a long operation would pin it in the LRU cache and
/// starve the rest of the store, so every method below acquires, works, and releases.
/// </para>
/// </summary>
internal sealed class ObjectStoreGarbageCollector : IChunkStoreGarbageCollector
{
    private readonly ObjectStore _store;

    public ObjectStoreGarbageCollector(ObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public IReadOnlyList<string> BucketPrefixes => ObjectStore.Prefixes;

    /// <inheritdoc/>
    public string WorkingDirectory => _store.GcWorkingDirectory;

    /// <inheritdoc/>
    public bool BucketHasData(GcBucketId bucket) => _store.BucketHasData(bucket);

    public async Task<GcBucketWatermark> SnapshotWatermarkAsync(GcBucketId bucket, CancellationToken ct = default)
    {
        using var lease = await _store.AcquireGcHandlerAsync(bucket, ct).ConfigureAwait(false);
        return await lease.Handler.SnapshotWatermarkAsync(ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<GcObjectRef> EnumerateBucketAsync(
        GcBucketId bucket,
        GcBucketWatermark watermark,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // An empty watermark means the bucket held nothing when the run started, so every
        // entry that exists now postdates it and none of them are collectable.
        if (watermark.FileNo == GcBucketWatermark.Empty.FileNo)
            yield break;

        Dictionary<Hash32, Indexing.IndexEntry> index;
        using (var lease = await _store.AcquireGcHandlerAsync(bucket, ct).ConfigureAwait(false))
        {
            // Materialised under the lease rather than streamed, so the handler is free again
            // immediately. A bucket index is small (tens of MB at a billion objects per store).
            index = await lease.Handler.SnapshotIndexAsync(ct).ConfigureAwait(false);
        }

        foreach (var (hash, entry) in index)
        {
            ct.ThrowIfCancellationRequested();

            if (watermark.Precedes(entry.FileNo, entry.Offset))
                yield return new GcObjectRef(hash, entry.FileNo, entry.Offset, entry.Length);
        }
    }

    public async Task<byte[]?> ReadFileDefinitionForMarkAsync(Hash32 fileHash, CancellationToken ct = default)
    {
        try
        {
            return await _store.ReadFileDefinitionBlobAsync(fileHash).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            // Not indexed: either never ingested, or already reclaimed by an earlier run.
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task<GcReclaimResult> ReclaimAsync(
        GcBucketId bucket,
        IReadOnlyCollection<GcObjectRef> doomed,
        GarbageCollectionOptions options,
        GcCompactionBudget budget,
        CancellationToken ct = default)
    {
        if (doomed.Count == 0)
            return new GcReclaimResult();

        using var lease = await _store.AcquireGcHandlerAsync(bucket, ct).ConfigureAwait(false);
        var result = await lease.Handler.ReclaimAsync(doomed, options, budget, ct).ConfigureAwait(false);

        // Prefix the bucket so a warning is actionable on its own; the handler only knows its
        // own directory, not which category/prefix pair the caller was working on.
        for (var i = 0; i < result.Warnings.Count; i++)
            result.Warnings[i] = $"{bucket}: {result.Warnings[i]}";

        return result;
    }

    /// <inheritdoc/>
    public (long TotalBytes, long FreeBytes) GetVolumeSpace() => _store.GetVolumeSpace();

    public IAsyncEnumerable<GcReleasePackageRef> EnumerateReleasePackagesAsync(CancellationToken ct = default)
        => _store.EnumerateReleasePackagesAsync(ct);

    public async Task<bool> DeleteReleasePackageAsync(string hashHex, CancellationToken ct = default)
    {
        try
        {
            return await _store.DeleteReleasePackageAsync(hashHex).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public async Task<GcPurgeResult> PurgeObsoletePacksAsync(TimeSpan drainWindow, CancellationToken ct = default)
    {
        var packsDeleted = 0;
        var packsDraining = 0;
        long bytesFreed = 0;

        foreach (var category in new[] { GcObjectCategory.Chunk, GcObjectCategory.FileDefinition })
        {
            foreach (var prefix in ObjectStore.Prefixes)
            {
                ct.ThrowIfCancellationRequested();

                var bucket = new GcBucketId(category, prefix);

                // Skip buckets that were never materialised — the vast majority on a small
                // store. Opening a handler would create the directory for nothing.
                if (!_store.BucketHasData(bucket))
                    continue;

                using var lease = await _store.AcquireGcHandlerAsync(bucket, ct).ConfigureAwait(false);
                var result = await lease.Handler.PurgeObsoletePacksAsync(drainWindow, ct).ConfigureAwait(false);

                packsDeleted  += result.PacksDeleted;
                packsDraining += result.PacksStillDraining;
                bytesFreed    += result.BytesFreed;
            }
        }

        return new GcPurgeResult(packsDeleted, bytesFreed, packsDraining);
    }
}
