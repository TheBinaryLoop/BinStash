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

namespace BinStash.Core.Entities;

/// <summary>
/// Generic background job entity. Uses a discriminator column (<see cref="JobType"/>)
/// and JSON payload columns (<see cref="JobData"/>, <see cref="ProgressData"/>) to
/// support multiple long-running job types without per-type tables.
///
/// <para>
/// Common lifecycle fields (status, timestamps, errors) live on this base entity.
/// Job-type-specific input and progress are stored as JSON in <see cref="JobData"/>
/// and <see cref="ProgressData"/> respectively.
/// </para>
/// </summary>
public class BackgroundJob
{
    public Guid Id { get; set; }

    /// <summary>
    /// Discriminator identifying the kind of job (e.g. "ReleaseUpgrade").
    /// </summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Pending;

    /// <summary>
    /// JSON payload containing job-type-specific input parameters.
    /// For a release upgrade this would include ChunkStoreId, TargetSerializerVersion, etc.
    /// </summary>
    public string? JobData { get; set; }

    /// <summary>
    /// JSON payload containing job-type-specific progress data.
    /// Updated periodically as the job runs. For a release upgrade this would include
    /// TotalReleases, ProcessedReleases, FailedReleases, BytesSaved, etc.
    /// </summary>
    public string? ProgressData { get; set; }

    /// <summary>
    /// JSON array of error details accumulated during execution.
    /// </summary>
    public string? ErrorDetails { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Lifecycle states for a <see cref="BackgroundJob"/>.
/// </summary>
public enum BackgroundJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Well-known job type discriminator values.
/// </summary>
public static class BackgroundJobTypes
{
    public const string ReleaseUpgrade = "ReleaseUpgrade";
    public const string ChunkStoreRebuild = "ChunkStoreRebuild";
    public const string ChunkStoreGc = "ChunkStoreGc";
}

/// <summary>
/// Typed input payload for a <see cref="BackgroundJobTypes.ChunkStoreGc"/> job.
/// Serialized to JSON and stored in <see cref="BackgroundJob.JobData"/>.
/// </summary>
public sealed class ChunkStoreGcJobData
{
    public Guid ChunkStoreId { get; set; }

    /// <summary>Report what would be collected without quarantining or reclaiming anything.</summary>
    public bool DryRun { get; set; }

    /// <summary>Mark and quarantine, but stop before physically reclaiming any bytes.</summary>
    public bool SkipReclaim { get; set; }

    /// <summary>
    /// Overrides the configured quarantine retention, in hours. Null uses the instance default.
    /// Shortening this trades recoverability for disk, so it is a per-run operator decision
    /// rather than something the job infers.
    /// </summary>
    public double? RetentionHoursOverride { get; set; }
}

/// <summary>
/// Typed progress payload for a <see cref="BackgroundJobTypes.ChunkStoreGc"/> job.
/// Serialized to JSON and stored in <see cref="BackgroundJob.ProgressData"/>.
/// </summary>
public sealed class ChunkStoreGcProgressData
{
    /// <summary>The phase the run is currently in.</summary>
    public string Phase { get; set; } = ChunkStoreGcPhases.Pending;

    public int TotalBuckets { get; set; }
    public int ProcessedBuckets { get; set; }

    /// <summary>Releases whose reachable set has been walked.</summary>
    public int MarkedReleases { get; set; }
    public int TotalReleases { get; set; }

    /// <summary>
    /// File definitions read and decoded while expanding releases to the chunks they reach.
    ///
    /// <para>
    /// Tracked separately because this is the long half of marking and it is not proportional to
    /// the release count: one release names many files, and the same file is named by many
    /// releases. Without its own counter the release figure stops moving while the run is still
    /// doing most of its work.
    /// </para>
    /// </summary>
    public long ResolvedFileDefinitions { get; set; }

    /// <summary>Prefix groups of file definitions resolved, and how many there are in total.</summary>
    public int ProcessedFileDefinitionGroups { get; set; }
    public int TotalFileDefinitionGroups { get; set; }

    /// <summary>Distinct objects found reachable from the roots.</summary>
    public long ReachableObjects { get; set; }

    /// <summary>Objects moved into quarantine by this run.</summary>
    public long QuarantinedObjects { get; set; }

    /// <summary>Physical bytes those quarantined objects occupy.</summary>
    public long QuarantinedBytes { get; set; }

    /// <summary>Objects whose bytes this run physically dropped (from any run's quarantine).</summary>
    public long ReclaimedObjects { get; set; }

    /// <summary>
    /// Bytes of dead pack entries this run dropped — the size of the garbage it identified and
    /// stopped carrying forward.
    ///
    /// <para>
    /// This is not the same as space returned to the filesystem, and the two are tracked
    /// separately on purpose. Compaction rewrites a pack's survivors into a new file, so the
    /// volume only shrinks once the superseded file is unlinked, which happens after a drain
    /// window and is usually a later run's doing. Adding the two together would report space
    /// twice and flatter a run that has freed nothing yet.
    /// </para>
    /// </summary>
    public long ReclaimedBytes { get; set; }

    /// <summary>Pack files rewritten to drop dead entries.</summary>
    public int PacksCompacted { get; set; }

    /// <summary>Pack files unlinked after their drain window elapsed.</summary>
    public int PacksDeleted { get; set; }

    /// <summary>Bytes actually returned to the filesystem by unlinking superseded pack files.</summary>
    public long PackBytesDeleted { get; set; }

    /// <summary>
    /// Objects whose tombstones an in-flight ingest reclaimed before this run could — counted
    /// because a persistently high number means the retention window is too short for the
    /// workload.
    /// </summary>
    public long ResurrectedObjects { get; set; }
}

/// <summary>
/// Well-known <see cref="ChunkStoreGcProgressData.Phase"/> values.
/// </summary>
public static class ChunkStoreGcPhases
{
    public const string Pending = "Pending";

    /// <summary>Capturing per-bucket append watermarks.</summary>
    public const string Snapshot = "Snapshot";

    /// <summary>Walking releases to the file definitions they name.</summary>
    public const string Mark = "Mark";

    /// <summary>
    /// Expanding those file definitions to the chunks they reach.
    ///
    /// <para>
    /// Its own phase rather than part of <see cref="Mark"/>: it is where a run spends most of its
    /// marking time, and reporting it as "Mark" leaves the release counter frozen at its final
    /// value for minutes while the run is demonstrably still working.
    /// </para>
    /// </summary>
    public const string Resolve = "Resolve";

    /// <summary>Quarantining everything the mark phase did not reach.</summary>
    public const string Sweep = "Sweep";

    /// <summary>Physically reclaiming objects whose quarantine has expired.</summary>
    public const string Reclaim = "Reclaim";

    public const string Completed = "Completed";
}

/// <summary>
/// Typed input payload for a <see cref="BackgroundJobTypes.ReleaseUpgrade"/> job.
/// Serialized to JSON and stored in <see cref="BackgroundJob.JobData"/>.
/// </summary>
public sealed class ReleaseUpgradeJobData
{
    public Guid ChunkStoreId { get; set; }
    public byte TargetSerializerVersion { get; set; }
}

/// <summary>
/// Typed progress payload for a <see cref="BackgroundJobTypes.ReleaseUpgrade"/> job.
/// Serialized to JSON and stored in <see cref="BackgroundJob.ProgressData"/>.
/// </summary>
public sealed class ReleaseUpgradeProgressData
{
    public int TotalReleases { get; set; }
    public int ProcessedReleases { get; set; }
    public int FailedReleases { get; set; }
    public int SkippedReleases { get; set; }
    public long BytesSaved { get; set; }
    public long BytesGrown { get; set; }
}

/// <summary>
/// Typed input payload for a <see cref="BackgroundJobTypes.ChunkStoreRebuild"/> job.
/// Serialized to JSON and stored in <see cref="BackgroundJob.JobData"/>.
/// </summary>
public sealed class ChunkStoreRebuildJobData
{
    public Guid ChunkStoreId { get; set; }
}

/// <summary>
/// Typed progress payload for a <see cref="BackgroundJobTypes.ChunkStoreRebuild"/> job.
/// Serialized to JSON and stored in <see cref="BackgroundJob.ProgressData"/>.
/// </summary>
public sealed class ChunkStoreRebuildProgressData
{
    public int TotalBuckets { get; set; }
    public int ProcessedBuckets { get; set; }
    public int FailedBuckets { get; set; }
}
