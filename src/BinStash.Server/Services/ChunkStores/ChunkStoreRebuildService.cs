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
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Services.ReleaseUpgrade;
using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Services.ChunkStores;

/// <summary>
/// Executes a <c>ChunkStoreRebuild</c> background job by scanning all pack-file buckets
/// (4096 prefixes × 2 categories = 8192 total) and rebuilding their LSM-tree index.
///
/// <para>
/// Progress is persisted to <see cref="BackgroundJob.ProgressData"/> and broadcast via
/// GraphQL subscriptions on topic <c>BackgroundJobProgress_{jobId}</c> after every 64
/// buckets, and always on completion or cancellation.
/// </para>
/// </summary>
public sealed class ChunkStoreRebuildService : IChunkStoreRebuildService
{
    // Broadcast frequency: emit a progress event after this many bucket completions
    private const int BroadcastEvery = 64;

    private readonly IServiceProvider _services;
    private readonly ITopicEventSender _eventSender;
    private readonly ILogger<ChunkStoreRebuildService> _logger;

    public ChunkStoreRebuildService(IServiceProvider services, ITopicEventSender eventSender, ILogger<ChunkStoreRebuildService> logger)
    {
        _services = services;
        _eventSender = eventSender;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var chunkStoreService = scope.ServiceProvider.GetRequiredService<IChunkStoreService>();

        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            _logger.LogError("Rebuild job {JobId} not found", jobId);
            return;
        }

        if (job.Status is BackgroundJobStatus.Cancelled)
        {
            _logger.LogInformation("Rebuild job {JobId} was cancelled before starting", jobId);
            return;
        }

        var jobData = JsonSerializer.Deserialize<ChunkStoreRebuildJobData>(job.JobData ?? "{}");
        if (jobData is null)
        {
            _logger.LogError("Rebuild job {JobId} has invalid JobData", jobId);
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorDetails = JsonSerializer.Serialize(new[] { new { Error = "Invalid JobData payload" } });
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgressAsync(job, jobData, CancellationToken.None);
            return;
        }

        var store = await db.ChunkStores.FindAsync([jobData.ChunkStoreId], cancellationToken);
        if (store is null)
        {
            _logger.LogError("Rebuild job {JobId}: ChunkStore {ChunkStoreId} not found", jobId, jobData.ChunkStoreId);
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorDetails = JsonSerializer.Serialize(new[] { new { Error = $"ChunkStore {jobData.ChunkStoreId} not found" } });
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgressAsync(job, jobData, CancellationToken.None);
            return;
        }

        // 4096 prefixes × 2 categories (chunks + fileDefs) = 8192 total buckets
        const int totalBuckets = 4096 * 2;
        var progress = new ChunkStoreRebuildProgressData { TotalBuckets = totalBuckets };

        // Mark running
        job.Status = BackgroundJobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.ProgressData = JsonSerializer.Serialize(progress);
        await db.SaveChangesAsync(cancellationToken);
        await BroadcastProgressAsync(job, jobData, cancellationToken);

        try
        {
            var bucketProgress = new Progress<bool>(succeeded =>
            {
                progress.ProcessedBuckets++;
                if (!succeeded) progress.FailedBuckets++;
            });

            // Run the rebuild, checking for user-requested cancellation at bucket boundaries.
            // The IProgress callback is synchronous so counters stay accurate throughout.
            // We also interleave a DB cancellation check every BroadcastEvery buckets by
            // wrapping the call and broadcasting from inside the service via a custom
            // IProgress that calls back into this method.  Because RebuildStorageWithProgressAsync
            // is sequential (not Task.WhenAll) we can check the token synchronously after each
            // Report() call.

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var broadcastProgress = new BroadcastingProgress(
                async (succeeded) =>
                {
                    progress.ProcessedBuckets++;
                    if (!succeeded) progress.FailedBuckets++;

                    // Periodically persist progress and broadcast
                    if (progress.ProcessedBuckets % BroadcastEvery == 0)
                    {
                        // Re-read job status from DB to detect user-issued cancellation
                        var freshStatus = await db.BackgroundJobs
                            .Where(j => j.Id == jobId)
                            .Select(j => j.Status)
                            .FirstAsync(CancellationToken.None);

                        if (freshStatus == BackgroundJobStatus.Cancelled)
                        {
                            _logger.LogInformation("Rebuild job {JobId} cancelled by user at bucket {Count}/{Total}",
                                jobId, progress.ProcessedBuckets, totalBuckets);
                            await linkedCts.CancelAsync();
                            return;
                        }

                        job.ProgressData = JsonSerializer.Serialize(progress);
                        await db.SaveChangesAsync(CancellationToken.None);
                        await BroadcastProgressAsync(job, jobData, CancellationToken.None);
                    }
                });

            bool allOk;
            try
            {
                allOk = await chunkStoreService.RebuildStorageWithProgressAsync(
                    store, broadcastProgress, linkedCts.Token);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // User-requested cancellation via DB status check
                job.Status = BackgroundJobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.ProgressData = JsonSerializer.Serialize(progress);
                await db.SaveChangesAsync(CancellationToken.None);
                await BroadcastProgressAsync(job, jobData, CancellationToken.None);
                return;
            }

            // Final completion
            job.Status = allOk && progress.FailedBuckets == 0
                ? BackgroundJobStatus.Completed
                : BackgroundJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressData = JsonSerializer.Serialize(progress);
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgressAsync(job, jobData, CancellationToken.None);

            _logger.LogInformation(
                "Rebuild job {JobId} {Status}: {Processed}/{Total} buckets, {Failed} failed",
                jobId, job.Status, progress.ProcessedBuckets, totalBuckets, progress.FailedBuckets);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = BackgroundJobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressData = JsonSerializer.Serialize(progress);
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgressAsync(job, jobData, CancellationToken.None);
            _logger.LogInformation("Rebuild job {JobId} cancelled via host shutdown token", jobId);
        }
        catch (Exception ex)
        {
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressData = JsonSerializer.Serialize(progress);
            job.ErrorDetails = JsonSerializer.Serialize(new[] { new { Error = ex.ToString() } });
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgressAsync(job, jobData, CancellationToken.None);
            _logger.LogError(ex, "Rebuild job {JobId} failed with unhandled exception", jobId);
        }
    }

    private async Task BroadcastProgressAsync(BackgroundJob job, ChunkStoreRebuildJobData? jobData, CancellationToken ct)
    {
        try
        {
            var progress = !string.IsNullOrEmpty(job.ProgressData)
                ? JsonSerializer.Deserialize<ChunkStoreRebuildProgressData>(job.ProgressData)
                : new ChunkStoreRebuildProgressData();

            var dto = new BackgroundJobProgressDto
            {
                JobId = job.Id,
                JobType = job.JobType,
                Status = job.Status.ToString(),
                TotalBuckets = progress?.TotalBuckets ?? 0,
                ProcessedBuckets = progress?.ProcessedBuckets ?? 0,
                FailedBuckets = progress?.FailedBuckets ?? 0,
                ChunkStoreId = jobData?.ChunkStoreId,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt
            };

            await _eventSender.SendAsync($"BackgroundJobProgress_{job.Id}", dto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast progress for rebuild job {JobId}", job.Id);
        }
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> that invokes an async callback on each report.
    /// Reports are sequential because <c>RebuildStorageWithProgressAsync</c> is sequential.
    /// </summary>
    private sealed class BroadcastingProgress : IProgress<bool>
    {
        private readonly Func<bool, Task> _callback;
        private Task _current = Task.CompletedTask;

        public BroadcastingProgress(Func<bool, Task> callback) => _callback = callback;

        public void Report(bool value)
        {
            // Chain tasks so we never run two progress operations concurrently.
            // The rebuild loop awaits each bucket before calling Report, so this is
            // effectively synchronous from the caller's perspective.
            _current = _current.ContinueWith(_ => _callback(value), TaskScheduler.Default).Unwrap();
            _current.GetAwaiter().GetResult();
        }
    }
}
