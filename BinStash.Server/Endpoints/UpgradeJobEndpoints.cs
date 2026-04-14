// Copyright (C) 2025-2026  Lukas Essmann
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

using System.Text.Json;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class UpgradeJobEndpoints
{
    public static RouteGroupBuilder MapUpgradeJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/upgrade-jobs")!
            .WithTags("UpgradeJobs")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireInstancePermission(InstancePermission.Admin);

        group.MapGet("/{id:guid}", GetUpgradeJobAsync)!
            .WithDescription("Gets the current status of an upgrade job.")
            .WithSummary("Get Upgrade Job")
            .Produces<UpgradeJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancel", CancelUpgradeJobAsync)!
            .WithDescription("Requests cancellation of a running upgrade job. The job will stop at the next batch boundary.")
            .WithSummary("Cancel Upgrade Job")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListUpgradeJobsAsync)!
            .WithDescription("Lists all upgrade jobs, optionally filtered by chunk store.")
            .WithSummary("List Upgrade Jobs")
            .Produces<List<UpgradeJobDto>>();

        return group;
    }

    private static async Task<IResult> GetUpgradeJobAsync(Guid id, BinStashDbContext db)
    {
        var job = await db.BackgroundJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.JobType == BackgroundJobTypes.ReleaseUpgrade);
        if (job is null)
            return Results.NotFound();

        return Results.Ok(MapToDto(job));
    }

    private static async Task<IResult> CancelUpgradeJobAsync(Guid id, BinStashDbContext db)
    {
        var job = await db.BackgroundJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.JobType == BackgroundJobTypes.ReleaseUpgrade);
        if (job is null)
            return Results.NotFound();

        if (job.Status is not (BackgroundJobStatus.Pending or BackgroundJobStatus.Running))
            return Results.Conflict("Job is not in a cancellable state.");

        job.Status = BackgroundJobStatus.Cancelled;
        await db.SaveChangesAsync();

        return Results.Accepted();
    }

    private static async Task<IResult> ListUpgradeJobsAsync(BinStashDbContext db, Guid? chunkStoreId = null)
    {
        var query = db.BackgroundJobs
            .Where(j => j.JobType == BackgroundJobTypes.ReleaseUpgrade);

        if (chunkStoreId.HasValue)
            query = query.Where(j => j.JobData != null && j.JobData.Contains(chunkStoreId.Value.ToString()));

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        return Results.Ok(jobs.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Maps a <see cref="BackgroundJob"/> with <see cref="BackgroundJobTypes.ReleaseUpgrade"/>
    /// type to the REST DTO by deserializing the JSON payload columns.
    /// </summary>
    internal static UpgradeJobDto MapToDto(BackgroundJob job)
    {
        var jobData = job.JobData is not null
            ? JsonSerializer.Deserialize<ReleaseUpgradeJobData>(job.JobData)
            : null;
        var progress = job.ProgressData is not null
            ? JsonSerializer.Deserialize<ReleaseUpgradeProgressData>(job.ProgressData)
            : null;

        return new UpgradeJobDto
        {
            Id = job.Id,
            ChunkStoreId = jobData?.ChunkStoreId ?? Guid.Empty,
            Status = job.Status.ToString(),
            TargetSerializerVersion = jobData?.TargetSerializerVersion ?? 0,
            TotalReleases = progress?.TotalReleases ?? 0,
            ProcessedReleases = progress?.ProcessedReleases ?? 0,
            FailedReleases = progress?.FailedReleases ?? 0,
            SkippedReleases = progress?.SkippedReleases ?? 0,
            BytesSaved = progress?.BytesSaved ?? 0,
            BytesGrown = progress?.BytesGrown ?? 0,
            ErrorDetails = job.ErrorDetails,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt
        };
    }
}

public sealed class UpgradeJobDto
{
    public Guid Id { get; init; }
    public Guid ChunkStoreId { get; init; }
    public required string Status { get; init; }
    public byte TargetSerializerVersion { get; init; }
    public int TotalReleases { get; init; }
    public int ProcessedReleases { get; init; }
    public int FailedReleases { get; init; }
    public int SkippedReleases { get; init; }
    public long BytesSaved { get; init; }
    public long BytesGrown { get; init; }
    public string? ErrorDetails { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
