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
