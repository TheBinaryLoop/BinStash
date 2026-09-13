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

namespace BinStash.Core.Storage.Gc;

/// <summary>
/// Tunables for an online chunk-store garbage-collection run.
///
/// <para>
/// The defaults are deliberately conservative: the whole point of the design is
/// that reclaiming space late costs only disk, whereas reclaiming it early costs
/// data. Every window below buys safety against a specific race, so shortening one
/// should be a deliberate operator decision rather than a tuning accident.
/// </para>
/// </summary>
public sealed class GarbageCollectionOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "ChunkStoreGc";

    /// <summary>
    /// How long a quarantined object stays recoverable before the reclaim phase may
    /// physically drop it.
    ///
    /// <para>
    /// This must comfortably exceed the longest possible gap between an ingest client
    /// being told "you already have this object" and that client finalising its release,
    /// because within that gap the client will not re-upload the object and relies on
    /// finalize-time resurrection to repair the reference. Ingest sessions expire after
    /// 30 minutes of inactivity but may be kept alive indefinitely by an active upload,
    /// so the default allows a full day of slack.
    /// </para>
    /// </summary>
    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a pack file that has been superseded by copy-forward compaction stays
    /// on disk before it is unlinked.
    ///
    /// <para>
    /// A reader resolves a hash to a <c>(fileNo, offset)</c> pair and only afterwards
    /// opens the pack file. This window bounds how long that gap may be. It needs to
    /// exceed the slowest single object read, not the slowest download — each pack read
    /// is one entry.
    /// </para>
    /// </summary>
    public TimeSpan ObsoletePackDrainWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Minimum fraction of a pack file's bytes that must be dead before the file is
    /// worth rewriting. Below this, copying the live entries forward costs more I/O
    /// than the reclaimed space is worth.
    /// </summary>
    public double MinimumPackGarbageRatio { get; set; } = 0.25;

    /// <summary>
    /// Upper bound on how many pack files a single run will rewrite, so that a run on a
    /// very fragmented store stays a bounded amount of background I/O. Remaining files
    /// are picked up by the next run.
    /// </summary>
    public int MaxPacksToCompactPerRun { get; set; } = 512;

    /// <summary>
    /// How many prefix buckets are marked or swept concurrently. Bounded because each
    /// worker holds an open pack-file handler and a bucket-sized mark set.
    /// </summary>
    public int BucketConcurrency { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>
    /// When true the run computes and reports everything but neither quarantines nor
    /// reclaims anything. Intended as the "what would this delete?" operator check.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// When true the run performs mark and sweep (quarantine) but stops short of the
    /// reclaim phase. Useful for draining a store in two supervised steps.
    /// </summary>
    public bool SkipReclaim { get; set; }

    /// <summary>
    /// Throws if any value would make the run unsafe or degenerate.
    /// </summary>
    public void Validate()
    {
        if (RetentionWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RetentionWindow), "Retention window cannot be negative.");

        if (ObsoletePackDrainWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ObsoletePackDrainWindow), "Drain window cannot be negative.");

        if (MinimumPackGarbageRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumPackGarbageRatio), "Garbage ratio must be between 0 and 1.");

        if (MaxPacksToCompactPerRun < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxPacksToCompactPerRun), "Pack compaction cap cannot be negative.");

        if (BucketConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(BucketConcurrency), "Bucket concurrency must be at least 1.");
    }
}
