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

using System.Text.Json;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class RebuildJobEndpoints
{
    public static RouteGroupBuilder MapRebuildJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rebuild-jobs")
            .WithTags("RebuildJobs")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireInstancePermission(InstancePermission.Admin);

        group.MapGet("/{id:guid}", GetRebuildJobAsync)
            .WithDescription("Gets the current status of a chunk-store rebuild job.")
            .WithSummary("Get Rebuild Job")
            .Produces<RebuildJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancel", CancelRebuildJobAsync)
            .WithDescription("Requests cancellation of a running rebuild job. The job will stop at the next bucket boundary.")
            .WithSummary("Cancel Rebuild Job")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListRebuildJobsAsync)
            .WithDescription("Lists all chunk-store rebuild jobs, optionally filtered by chunk store.")
            .WithSummary("List Rebuild Jobs")
            .Produces<List<RebuildJobDto>>();

        return group;
    }

    private static async Task<IResult> GetRebuildJobAsync(Guid id, BinStashDbContext db)
    {
        var job = await db.BackgroundJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.JobType == BackgroundJobTypes.ChunkStoreRebuild);
        if (job is null)
            return Results.NotFound();

        return Results.Ok(MapToDto(job));
    }

    private static async Task<IResult> CancelRebuildJobAsync(Guid id, BinStashDbContext db)
    {
        var job = await db.BackgroundJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.JobType == BackgroundJobTypes.ChunkStoreRebuild);
        if (job is null)
            return Results.NotFound();

        if (job.Status is not (BackgroundJobStatus.Pending or BackgroundJobStatus.Running))
            return Results.Conflict("Job is not in a cancellable state.");

        job.Status = BackgroundJobStatus.Cancelled;
        await db.SaveChangesAsync();

        return Results.Accepted();
    }

    private static async Task<IResult> ListRebuildJobsAsync(BinStashDbContext db, Guid? chunkStoreId = null)
    {
        var jobs = await db.BackgroundJobs
            .Where(j => j.JobType == BackgroundJobTypes.ChunkStoreRebuild)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        var dtos = jobs.Select(MapToDto).ToList();

        // Filter by ChunkStoreId after materialization because JobData is a jsonb
        // column and EF Core cannot translate string Contains/LIKE against jsonb.
        if (chunkStoreId.HasValue)
            dtos = dtos.Where(d => d.ChunkStoreId == chunkStoreId.Value).ToList();

        return Results.Ok(dtos);
    }

    /// <summary>
    /// Maps a <see cref="BackgroundJob"/> with <see cref="BackgroundJobTypes.ChunkStoreRebuild"/>
    /// type to the REST DTO by deserializing the JSON payload columns.
    /// </summary>
    internal static RebuildJobDto MapToDto(BackgroundJob job)
    {
        var jobData = job.JobData is not null
            ? JsonSerializer.Deserialize<ChunkStoreRebuildJobData>(job.JobData)
            : null;
        var progress = job.ProgressData is not null
            ? JsonSerializer.Deserialize<ChunkStoreRebuildProgressData>(job.ProgressData)
            : null;

        return new RebuildJobDto
        {
            Id = job.Id,
            ChunkStoreId = jobData?.ChunkStoreId ?? Guid.Empty,
            Status = job.Status.ToString(),
            TotalBuckets = progress?.TotalBuckets ?? 0,
            ProcessedBuckets = progress?.ProcessedBuckets ?? 0,
            FailedBuckets = progress?.FailedBuckets ?? 0,
            ErrorDetails = job.ErrorDetails,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt
        };
    }
}

public sealed class RebuildJobDto
{
    public Guid Id { get; init; }
    public Guid ChunkStoreId { get; init; }
    public required string Status { get; init; }
    public int TotalBuckets { get; init; }
    public int ProcessedBuckets { get; init; }
    public int FailedBuckets { get; init; }
    public string? ErrorDetails { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
