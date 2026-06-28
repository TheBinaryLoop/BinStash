// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text.Json;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.Jobs;

public sealed class BackgroundJobService(
    BinStashDbContext db,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService)
{
    private HttpContext HttpContext => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("No HTTP context available.");

    /// <summary>
    /// Returns all background jobs, optionally filtered by job type and/or chunk store ID.
    /// Requires instance admin.
    /// </summary>
    public async Task<IQueryable<BackgroundJobGql>> GetBackgroundJobsAsync(
        string? jobType,
        Guid? chunkStoreId,
        CancellationToken cancellationToken)
    {
        await GraphQlAuth.EnsureInstancePermissionAsync(HttpContext.User, authorizationService, InstancePermission.Admin);

        var jobs = await db.BackgroundJobs
            .AsNoTracking()
            .Where(j => jobType == null || j.JobType == jobType)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        var gqlJobs = jobs
            .Select(MapToGql)
            .Where(j => chunkStoreId == null || j.ChunkStoreId == chunkStoreId.Value)
            .AsQueryable();

        return gqlJobs;
    }

    /// <summary>
    /// Returns a single background job by ID.
    /// Requires instance admin.
    /// </summary>
    public async Task<BackgroundJobGql?> GetBackgroundJobAsync(Guid id, CancellationToken cancellationToken)
    {
        await GraphQlAuth.EnsureInstancePermissionAsync(HttpContext.User, authorizationService, InstancePermission.Admin);

        var job = await db.BackgroundJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        return job is null ? null : MapToGql(job);
    }

    /// <summary>
    /// Cancels a pending or running background job by ID.
    /// Requires instance admin.
    /// </summary>
    public async Task<BackgroundJobGql> CancelBackgroundJobAsync(Guid id, CancellationToken cancellationToken)
    {
        await GraphQlAuth.EnsureInstancePermissionAsync(HttpContext.User, authorizationService, InstancePermission.Admin);

        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken)
            ?? throw new GraphQLException(ErrorBuilder.New()
                .SetMessage($"Background job '{id}' not found.")
                .SetCode("NOT_FOUND")
                .Build());

        if (job.Status is not (BackgroundJobStatus.Pending or BackgroundJobStatus.Running))
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Job is not in a cancellable state.")
                .SetCode("CONFLICT")
                .Build());

        job.Status = BackgroundJobStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);

        return MapToGql(job);
    }

    internal static BackgroundJobGql MapToGql(BackgroundJob job)
    {
        RebuildJobProgressGql? rebuildProgress = null;
        UpgradeJobProgressGql? upgradeProgress = null;

        if (job.JobType == BackgroundJobTypes.ChunkStoreRebuild)
        {
            var jobData = job.JobData is not null
                ? JsonSerializer.Deserialize<ChunkStoreRebuildJobData>(job.JobData)
                : null;
            var progress = job.ProgressData is not null
                ? JsonSerializer.Deserialize<ChunkStoreRebuildProgressData>(job.ProgressData)
                : null;

            rebuildProgress = new RebuildJobProgressGql
            {
                TotalBuckets = progress?.TotalBuckets ?? 0,
                ProcessedBuckets = progress?.ProcessedBuckets ?? 0,
                FailedBuckets = progress?.FailedBuckets ?? 0,
            };

            return new BackgroundJobGql
            {
                Id = job.Id,
                JobType = job.JobType,
                Status = job.Status.ToString(),
                ChunkStoreId = jobData?.ChunkStoreId ?? Guid.Empty,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                ErrorDetails = job.ErrorDetails,
                RebuildProgress = rebuildProgress,
            };
        }
        else if (job.JobType == BackgroundJobTypes.ReleaseUpgrade)
        {
            var jobData = job.JobData is not null
                ? JsonSerializer.Deserialize<ReleaseUpgradeJobData>(job.JobData)
                : null;
            var progress = job.ProgressData is not null
                ? JsonSerializer.Deserialize<ReleaseUpgradeProgressData>(job.ProgressData)
                : null;

            upgradeProgress = new UpgradeJobProgressGql
            {
                TargetSerializerVersion = jobData?.TargetSerializerVersion ?? 0,
                TotalReleases = progress?.TotalReleases ?? 0,
                ProcessedReleases = progress?.ProcessedReleases ?? 0,
                FailedReleases = progress?.FailedReleases ?? 0,
                SkippedReleases = progress?.SkippedReleases ?? 0,
                BytesSaved = progress?.BytesSaved ?? 0,
                BytesGrown = progress?.BytesGrown ?? 0,
            };

            return new BackgroundJobGql
            {
                Id = job.Id,
                JobType = job.JobType,
                Status = job.Status.ToString(),
                ChunkStoreId = jobData?.ChunkStoreId ?? Guid.Empty,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                ErrorDetails = job.ErrorDetails,
                UpgradeProgress = upgradeProgress,
            };
        }

        // Generic / unknown job type
        return new BackgroundJobGql
        {
            Id = job.Id,
            JobType = job.JobType,
            Status = job.Status.ToString(),
            ChunkStoreId = Guid.Empty,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ErrorDetails = job.ErrorDetails,
        };
    }
}
