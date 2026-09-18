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
    /// <summary>
    /// Configuration section these options bind from.
    /// </summary>
    /// <remarks>
    /// Every default below is expressed here in code and deliberately <em>not</em> mirrored into
    /// <c>appsettings.json</c>. The database-backed configuration provider that instance settings
    /// write to is registered at the lowest priority, so a key present in appsettings wins over
    /// the database — a default shipped there would silently overwrite the operator's saved value
    /// on the next restart. Keeping the defaults in code leaves appsettings and environment
    /// variables free to do what they are for: pinning a value so the UI cannot change it.
    /// </remarks>
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
    /// Ceiling on the total size of the pack files one run may rewrite, across every bucket.
    /// Set to 0 for no limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the knob that bounds how much extra disk a run needs. Compaction writes the
    /// survivors of a pack to a new file and only unlinks the original after its drain window,
    /// so the two coexist and a run's peak overhead is the size of everything it rewrites.
    /// <see cref="MaxPacksToCompactPerRun"/> does not bound that on its own: it is applied per
    /// bucket, and a store has thousands of buckets holding a handful of packs each.
    /// </para>
    /// <para>
    /// Whatever does not fit keeps its tombstones and is collected by a later run, so the effect
    /// of a smaller budget is that reclaiming a large backlog takes more runs — not that any of
    /// it is lost. The default is deliberately modest so that the first run on a store that has
    /// never been collected cannot ask for more room than the volume has.
    /// </para>
    /// </remarks>
    public long MaxBytesToCompactPerRun { get; set; } = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// Fraction of the store volume that compaction will not eat into, whatever
    /// <see cref="MaxBytesToCompactPerRun"/> allows. Set to 0 to disable the check.
    /// </summary>
    /// <remarks>
    /// The budget is the smaller of the two, so a nearly full volume throttles itself rather
    /// than relying on the operator having picked a byte figure that suits it. Compaction is the
    /// one part of collection that needs space *before* it returns any, which is exactly when
    /// running out is least recoverable.
    /// </remarks>
    public double CompactionFreeSpaceReserveFraction { get; set; } = 0.10;

    /// <summary>
    /// A pack at or below this size is a candidate for being merged into a larger one, whether
    /// or not it contains any garbage. Set to 0 to never merge on size alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compaction rewrites a pack's survivors into a fresh file, so on its own it holds the file
    /// count level — it never brings it down. Each seal-then-compact cycle therefore leaves
    /// another part-full survivor pack behind, and a bucket under steady churn accumulates them
    /// without bound, each costing a file handle and a seek.
    /// </para>
    /// <para>
    /// Folding the small ones into a single output is what bounds that. The merged output is
    /// still capped by <see cref="MaxMergedPackBytes"/>, so consolidation cannot produce a pack
    /// larger than the rollover would have allowed.
    /// </para>
    /// </remarks>
    public long MergePacksBelowBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// Ceiling on the combined size of the sources a single merge will read, which bounds both
    /// the output pack and the I/O one run may spend consolidating a bucket.
    /// </summary>
    public long MaxMergedPackBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>
    /// Whether a run may seal the pack a bucket is appending to when that pack has accumulated
    /// enough garbage to be worth rewriting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compaction never rewrites the append target, because doing so would race the writer. On
    /// a bucket that has only ever needed one pack file, that rule has no escape: the single
    /// pack is permanently the append target, so its garbage is deferred on every run and the
    /// space is never returned. Packs roll over at 4 GiB, so a bucket well under that size
    /// stays in this state indefinitely.
    /// </para>
    /// <para>
    /// Sealing closes the current pack and starts a new one for subsequent appends. It moves no
    /// bytes and touches no existing entry — the sealed pack simply stops being the append
    /// target, which is enough for the ordinary copy-forward path to compact it on the next
    /// run. Turn this off to restore the previous behaviour of deferring indefinitely.
    /// </para>
    /// </remarks>
    public bool SealAppendPackForCompaction { get; set; } = true;

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
    /// Governs the unattended runs the scheduler queues. Disabled by default: collection is
    /// destructive at the end of its retention window, so an instance must opt in to it rather
    /// than inherit it from an upgrade.
    /// </summary>
    public GcScheduleOptions Schedule { get; set; } = new();

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

        if (MaxBytesToCompactPerRun < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxBytesToCompactPerRun), "Compaction byte budget cannot be negative.");

        if (CompactionFreeSpaceReserveFraction is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(CompactionFreeSpaceReserveFraction), "Free space reserve must be in [0, 1).");

        if (MergePacksBelowBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(MergePacksBelowBytes), "Merge threshold cannot be negative.");

        if (MaxMergedPackBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxMergedPackBytes), "Merged pack ceiling must be positive.");

        if (BucketConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(BucketConcurrency), "Bucket concurrency must be at least 1.");

        Schedule.Validate();
    }
}

/// <summary>
/// When, and how often, the instance collects its chunk stores without being asked.
///
/// <para>
/// A scheduled run is the same run an operator triggers by hand — same phases, same safety
/// gates. The only thing this adds is <em>when</em>, which matters because collection is
/// sustained background I/O against the same disks that serve ingest and download.
/// </para>
/// </summary>
public sealed class GcScheduleOptions
{
    /// <summary>
    /// Whether the scheduler queues runs at all.
    ///
    /// <para>
    /// Off by default, and deliberately so. Every other knob here only moves work around in
    /// time; this one decides whether an instance starts destroying content it was not
    /// destroying yesterday. An upgrade must not make that decision on an operator's behalf.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Minimum age of the last run before a store is collected again.
    ///
    /// <para>
    /// Measured from the previous run's <em>start</em>, not its completion, so a store whose
    /// collection takes hours still gets a predictable cadence rather than drifting later with
    /// every run.
    /// </para>
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// First hour (UTC, 0–23) of the daily window in which runs may <em>start</em>. Null, or a
    /// null <see cref="WindowEndHourUtc"/>, means any hour is acceptable.
    ///
    /// <para>
    /// Only the start is gated. A run that begins inside the window and outlives it is left
    /// alone: aborting collection part-way through would throw away the mark phase's work and
    /// leave quarantine decisions half-made, which costs more than the I/O it would save.
    /// </para>
    /// </summary>
    public int? WindowStartHourUtc { get; set; }

    /// <summary>
    /// Hour (UTC, 0–23) the window closes, exclusive. May be smaller than
    /// <see cref="WindowStartHourUtc"/>, which expresses a window that crosses midnight —
    /// 22 to 4 is the usual overnight case.
    /// </summary>
    public int? WindowEndHourUtc { get; set; }

    /// <summary>
    /// Queue scheduled runs as dry runs: they report what they would collect and change nothing.
    /// The honest way to watch a real workload for a week before letting collection bite.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Queue scheduled runs that quarantine but never reclaim. Unreachable content stops being
    /// deduplicated against and stops growing, while every byte stays recoverable until an
    /// operator runs a reclaiming pass by hand.
    /// </summary>
    public bool SkipReclaim { get; set; }

    /// <summary>
    /// True when <paramref name="utcNow"/> falls inside the configured window, including the
    /// wrap-around case where the window crosses midnight. An incompletely configured window is
    /// treated as no window at all rather than as a window that never opens — failing to run is
    /// the less obvious failure, so it must not be the one a half-filled form produces.
    /// </summary>
    public bool IsWithinWindow(DateTimeOffset utcNow)
    {
        if (WindowStartHourUtc is not { } start || WindowEndHourUtc is not { } end || start == end)
            return true;

        var hour = utcNow.UtcDateTime.Hour;

        return start < end
            ? hour >= start && hour < end
            : hour >= start || hour < end;
    }

    /// <summary>
    /// True when a store whose last run started at <paramref name="lastRunStartedAt"/> is due
    /// again. A store that has never been collected is due as soon as the window allows.
    /// </summary>
    public bool IsDue(DateTimeOffset utcNow, DateTimeOffset? lastRunStartedAt)
    {
        if (!Enabled || !IsWithinWindow(utcNow))
            return false;

        return lastRunStartedAt is not { } last || utcNow - last >= Interval;
    }

    /// <summary>Throws if any value would make the schedule unsafe or degenerate.</summary>
    public void Validate()
    {
        if (Enabled && Interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Interval), "Scheduled collection interval must be positive.");

        if (WindowStartHourUtc is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(WindowStartHourUtc), "Window start hour must be between 0 and 23.");

        if (WindowEndHourUtc is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(WindowEndHourUtc), "Window end hour must be between 0 and 23.");
    }
}
