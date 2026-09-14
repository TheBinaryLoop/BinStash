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
using BinStash.Core.Auditing;
using BinStash.Core.Entities;
using BinStash.Core.Storage.Gc;
using BinStash.Infrastructure.Data;
using BinStash.Server.Services.ChunkStores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.HostedServices;

/// <summary>
/// Queues unattended garbage-collection runs for every chunk store on the configured cadence.
///
/// <para>
/// This service decides <em>whether</em> and <em>when</em> only; it queues onto the same
/// <see cref="GcJobChannel"/> an operator's mutation writes to, so a scheduled run and a manual
/// one are the same job executed by the same serial worker. That is what keeps the schedule from
/// needing its own concurrency story.
/// </para>
/// <para>
/// It is deliberately not a cron. Collection is idempotent and its cost is proportional to what
/// has changed, so "has it been long enough since the last run?" is both simpler and more robust
/// across restarts than remembering firing times — an instance that was down all night collects
/// once when it comes back rather than replaying missed slots.
/// </para>
/// </summary>
public sealed class ChunkStoreGcSchedulerService : BackgroundService
{
    /// <summary>
    /// How often due-ness is re-evaluated. Fine enough that an hour-granular window is honoured
    /// with room to spare, coarse enough that the poll itself is free.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<GarbageCollectionOptions> _options;
    private readonly GcJobChannel _channel;
    private readonly ILogger<ChunkStoreGcSchedulerService> _logger;

    public ChunkStoreGcSchedulerService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<GarbageCollectionOptions> options,
        GcJobChannel channel,
        ILogger<ChunkStoreGcSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        // Settle first. Starting a store-wide read of every release the instant the process comes
        // up would compete with the startup work that actually has users waiting on it.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A scheduler that dies takes the schedule with it, silently. Log and keep ticking.
                _logger.LogError(ex, "Scheduled garbage-collection tick failed");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // Re-read every tick rather than caching: the schedule is editable at runtime through
        // instance settings, and an admin who turns collection off expects that to take effect
        // without a restart.
        var schedule = _options.CurrentValue.Schedule;

        if (!schedule.Enabled)
            return;

        var now = DateTimeOffset.UtcNow;
        if (!schedule.IsWithinWindow(now))
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();

        var stores = await db.ChunkStores
            .AsNoTracking()
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        foreach (var store in stores)
        {
            ct.ThrowIfCancellationRequested();

            var lastRunStartedAt = await ChunkStoreJobQueries.LastGcRunStartedAtAsync(db, store.Id, ct);
            if (!schedule.IsDue(now, lastRunStartedAt))
                continue;

            // Checked last, and per store: the answer is only meaningful immediately before
            // queuing, and one busy store must not hold up the others.
            if (await ChunkStoreJobQueries.HasActiveMaintenanceJobAsync(db, store.Id, ct))
            {
                _logger.LogDebug(
                    "Skipping scheduled collection of chunk store {ChunkStoreId}: a maintenance job is already queued or running",
                    store.Id);
                continue;
            }

            await QueueRunAsync(db, audit, schedule, store.Id, store.Name, lastRunStartedAt, ct);
        }
    }

    private async Task QueueRunAsync(
        BinStashDbContext db,
        IAuditLogWriter audit,
        GcScheduleOptions schedule,
        Guid chunkStoreId,
        string storeName,
        DateTimeOffset? lastRunStartedAt,
        CancellationToken ct)
    {
        var jobData = new ChunkStoreGcJobData
        {
            ChunkStoreId = chunkStoreId,
            DryRun = schedule.DryRun,
            SkipReclaim = schedule.SkipReclaim
            // RetentionHoursOverride stays null on purpose. Shortening retention is what makes a
            // run destructive sooner, and that is an operator's decision to take per run, never
            // something a recurring schedule quietly applies every day.
        };

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            JobType = BackgroundJobTypes.ChunkStoreGc,
            Status = BackgroundJobStatus.Pending,
            JobData = JsonSerializer.Serialize(jobData),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);

        await _channel.Channel.Writer.WriteAsync(job.Id, ct);

        _logger.LogInformation(
            "Queued scheduled garbage collection {JobId} for chunk store {ChunkStoreId} (last run {LastRun}, interval {Interval})",
            job.Id, chunkStoreId, lastRunStartedAt?.ToString("O") ?? "never", schedule.Interval);

        // Audited like the manual mutation is, and distinguishable from it by actor. An operator
        // looking at reclaimed space needs to be able to tell "the schedule did this" from
        // "someone did this".
        await audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.ChunkStoreGcStarted,
            InstanceScoped = true,
            SystemActor = true,
            SystemActorDisplay = "garbage-collection schedule",
            TargetType = nameof(ChunkStore),
            TargetId = chunkStoreId.ToString(),
            TargetName = storeName,
            Metadata = new Dictionary<string, object?>
            {
                ["jobId"] = job.Id,
                ["scheduled"] = true,
                ["dryRun"] = schedule.DryRun,
                ["skipReclaim"] = schedule.SkipReclaim,
                ["intervalHours"] = schedule.Interval.TotalHours
            }
        }, ct);
    }
}
