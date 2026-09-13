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

using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;

namespace BinStash.Core.Storage.Gc;

/// <summary>
/// The storage-side half of the online garbage collector: everything that has to touch
/// pack files and indexes, with none of the catalogue or reachability logic.
///
/// <para>
/// Every operation here is safe to call while the store is serving reads and writes.
/// The two rules that make that true are worth stating up front, because every method
/// below depends on them:
/// </para>
/// <list type="number">
///   <item>
///     <strong>Pack files are never modified in place.</strong> Reclaim copies the live
///     entries of a pack forward into a fresh pack, republishes the index, and only then
///     retires the old file — after a drain window, so that a reader that already resolved
///     an address still finds its bytes.
///   </item>
///   <item>
///     <strong>Nothing written after a run started is ever a candidate.</strong> Each bucket
///     is watermarked at the start of the run (see <see cref="GcBucketWatermark"/>), which
///     makes "was this here before we looked?" an exact question rather than a timing guess.
///   </item>
/// </list>
/// <para>
/// A backend that cannot honour those rules should not implement this interface; the
/// collector degrades to doing nothing rather than to doing something unsafe.
/// </para>
/// </summary>
public interface IChunkStoreGarbageCollector
{
    /// <summary>
    /// The three-hex-character prefixes a bucket category is partitioned into, in a stable
    /// order. Used to drive the mark and sweep loops and to report progress.
    /// </summary>
    IReadOnlyList<string> BucketPrefixes { get; }

    /// <summary>
    /// A directory the collector may use for scratch files, on the same volume as the store.
    ///
    /// <para>
    /// The mark phase spills its reachable set to disk, and that spill is sized in proportion to
    /// the store, not to the machine — a store with a billion chunks produces tens of gigabytes
    /// of marks. The system temp directory is the wrong place for that: it is frequently a tmpfs,
    /// where "spilling to disk" would quietly mean filling RAM. Anchoring the scratch beside the
    /// data it describes makes the space requirement scale with the thing that caused it.
    /// </para>
    /// </summary>
    string WorkingDirectory { get; }

    /// <summary>
    /// True when a bucket holds anything at all. Buckets are created lazily, so on most stores
    /// the large majority of the 8192 are empty and can be skipped without opening them.
    /// </summary>
    bool BucketHasData(GcBucketId bucket);

    /// <summary>
    /// Captures a bucket's append position. Call once per bucket at the start of a run,
    /// before marking, and pass the result to every later phase for that bucket.
    /// </summary>
    Task<GcBucketWatermark> SnapshotWatermarkAsync(GcBucketId bucket, CancellationToken ct = default);

    /// <summary>
    /// Enumerates every object indexed in a bucket whose bytes were already stored when
    /// <paramref name="watermark"/> was taken. Objects at or beyond the watermark are
    /// skipped: they are newer than the run and are implicitly live.
    /// </summary>
    IAsyncEnumerable<GcObjectRef> EnumerateBucketAsync(GcBucketId bucket, GcBucketWatermark watermark, CancellationToken ct = default);

    /// <summary>
    /// Reads a file-definition blob straight out of the pack store, bypassing the catalogue.
    /// The mark phase needs this because a quarantined file definition has no catalogue row
    /// yet may still be reachable from a release that is about to resurrect it.
    /// Returns <see langword="null"/> when the blob is genuinely absent.
    /// </summary>
    Task<byte[]?> ReadFileDefinitionForMarkAsync(Hash32 fileHash, CancellationToken ct = default);

    /// <summary>
    /// Physically drops <paramref name="doomed"/> from a bucket by copy-forward compaction,
    /// and republishes the bucket index without them.
    ///
    /// <para>
    /// Only pack files that are both sufficiently dead (see
    /// <see cref="GarbageCollectionOptions.MinimumPackGarbageRatio"/>) and no longer being
    /// appended to are rewritten. Objects in any other pack are reported as
    /// <see cref="GcReclaimResult.DeferredObjects"/> and left untouched, so the caller must
    /// keep their tombstones rather than assume the whole set was handled.
    /// </para>
    /// </summary>
    Task<GcReclaimResult> ReclaimAsync(GcBucketId bucket, IReadOnlyCollection<GcObjectRef> doomed, GarbageCollectionOptions options, CancellationToken ct = default);

    /// <summary>
    /// Unlinks pack files superseded by an earlier reclaim whose drain window has elapsed.
    /// Idempotent and independent of any run, so it is also the crash-recovery path: a run
    /// that dies after copying forward but before retiring leaves files that this call
    /// eventually cleans up.
    /// </summary>
    Task<GcPurgeResult> PurgeObsoletePacksAsync(TimeSpan drainWindow, CancellationToken ct = default);

    /// <summary>
    /// Enumerates every stored release-definition package.
    ///
    /// <para>
    /// These are plain files rather than pack entries, and they can be orphaned independently
    /// of any chunk — a release upgrade rewrites a package and deletes the old one, so a crash
    /// between those two steps strands the original. The collector treats a package as garbage
    /// only when no release row names it <em>and</em> it is older than the retention window,
    /// which keeps a package written seconds ago by an ingest that has not committed its release
    /// row yet out of reach.
    /// </para>
    /// </summary>
    IAsyncEnumerable<GcReleasePackageRef> EnumerateReleasePackagesAsync(CancellationToken ct = default);

    /// <summary>Deletes one release-definition package. Returns false if it was already gone.</summary>
    Task<bool> DeleteReleasePackageAsync(string hashHex, CancellationToken ct = default);
}
