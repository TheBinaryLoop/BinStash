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
/// Identifies one pack-store bucket: a category plus a three-hex-character prefix.
/// A store has 4096 prefixes per category.
/// </summary>
public readonly record struct GcBucketId(GcObjectCategory Category, string Prefix)
{
    public override string ToString() => $"{Category}/{Prefix}";
}

/// <summary>
/// The append position of a bucket at the instant a garbage-collection run started.
///
/// <para>
/// Pack entries are only ever appended, so position is a total order on write time:
/// anything stored at or beyond this watermark was written <em>after</em> the run began
/// and therefore cannot have been reachable from the roots the run enumerated. Excluding
/// it costs nothing (the next run will consider it) and removes an entire class of race
/// without needing a clock, a lock, or a per-object timestamp.
/// </para>
/// </summary>
/// <param name="FileNo">Pack file the bucket was appending to.</param>
/// <param name="Offset">Length of that pack file at snapshot time.</param>
public readonly record struct GcBucketWatermark(int FileNo, long Offset)
{
    /// <summary>An empty bucket: nothing can precede it, so nothing is ever a candidate.</summary>
    public static readonly GcBucketWatermark Empty = new(int.MinValue, 0);

    /// <summary>
    /// True when an entry at <paramref name="fileNo"/>/<paramref name="offset"/> was already
    /// stored when the run started, and so may be considered for collection.
    /// </summary>
    public bool Precedes(int fileNo, long offset)
        => fileNo < FileNo || (fileNo == FileNo && offset < Offset);
}

/// <summary>
/// One indexed object and where its bytes live.
/// </summary>
public readonly record struct GcObjectRef(Hash32 Hash, int FileNo, long Offset, int Length);

/// <summary>
/// What a bucket's reclaim pass actually did. All counts are physical, not logical.
/// </summary>
public sealed class GcReclaimResult
{
    /// <summary>Objects whose bytes were physically dropped.</summary>
    public List<Hash32> ReclaimedHashes { get; } = [];

    /// <summary>Bytes freed once the superseded pack files finish draining.</summary>
    public long ReclaimedBytes { get; set; }

    /// <summary>Pack files rewritten (their live entries copied forward).</summary>
    public int PacksCompacted { get; set; }

    /// <summary>Pack files that turned out to be entirely dead and were retired whole.</summary>
    public int PacksRetired { get; set; }

    /// <summary>
    /// Packs the pass tried and failed to rewrite, with the reason.
    ///
    /// <para>
    /// A failed compaction is not fatal — the tombstones survive and a later run retries — but it
    /// must not be silent. A pack that keeps failing is corruption or a full disk, and the
    /// symptom on its own is just "collection is not freeing anything".
    /// </para>
    /// </summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Objects that were <em>not</em> reclaimed this pass — typically because their bytes sit
    /// in the pack the bucket is currently appending to, which is never rewritten. Their
    /// tombstones stay in place for a later run.
    /// </summary>
    public int DeferredObjects { get; set; }
}

/// <summary>
/// A stored release-definition package (<c>.rdef</c>) as seen by the collector.
/// </summary>
/// <param name="HashHex">BLAKE3 of the package bytes — its filename and its identity.</param>
/// <param name="Bytes">Size on disk.</param>
/// <param name="LastWriteUtc">
/// When the file was last written. Release packages are immutable once stored, so this doubles
/// as the creation time and is what keeps a package written moments ago — by a release row that
/// has not been committed yet — out of reach of the sweep.
/// </param>
public readonly record struct GcReleasePackageRef(string HashHex, long Bytes, DateTime LastWriteUtc);

/// <summary>
/// Outcome of deleting pack files that copy-forward compaction superseded.
/// </summary>
/// <param name="PacksDeleted">Pack files unlinked.</param>
/// <param name="BytesFreed">Bytes returned to the filesystem.</param>
/// <param name="PacksStillDraining">Pack files whose drain window has not elapsed yet.</param>
public readonly record struct GcPurgeResult(int PacksDeleted, long BytesFreed, int PacksStillDraining);
