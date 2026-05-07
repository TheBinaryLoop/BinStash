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

using System.Threading.Channels;
using BinStash.Core.Entities;
using BinStash.Server.Services.ReleaseUpgrade;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.HostedServices;

/// <summary>
/// Long-running background service that drains the <see cref="Channel{Guid}"/> job queue
/// and executes upgrade jobs one at a time via <see cref="IReleaseUpgradeService"/>.
/// 
/// On startup it also resumes any jobs left in <c>Pending</c> or <c>Running</c> state
/// from a previous server lifecycle (crash recovery).
/// </summary>
public sealed class ReleaseUpgradeBackgroundService : BackgroundService
{
    private readonly ChannelReader<Guid> _reader;
    private readonly IServiceProvider _services;
    private readonly ILogger<ReleaseUpgradeBackgroundService> _logger;

    public ReleaseUpgradeBackgroundService(Channel<Guid> jobChannel, IServiceProvider services, ILogger<ReleaseUpgradeBackgroundService> logger)
    {
        _reader = jobChannel.Reader;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resume jobs that were in-flight when the server last stopped
        await ResumeIncompleteJobsAsync(stoppingToken);

        try
        {
            await foreach (var jobId in _reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("Dequeued upgrade job {JobId}", jobId);

                try
                {
                    using var scope = _services.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IReleaseUpgradeService>();
                    await service.ExecuteAsync(jobId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error executing upgrade job {JobId}", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// On startup, find any ReleaseUpgrade jobs stuck in Pending or Running state and re-enqueue them.
    /// </summary>
    private async Task ResumeIncompleteJobsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.BinStashDbContext>();
            var incompleteJobIds = await db.BackgroundJobs
                .Where(j => j.JobType == BackgroundJobTypes.ReleaseUpgrade
                            && (j.Status == BackgroundJobStatus.Pending
                                || j.Status == BackgroundJobStatus.Running))
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .ToListAsync(ct);

            foreach (var jobId in incompleteJobIds)
            {
                _logger.LogInformation("Resuming incomplete upgrade job {JobId} from previous lifecycle", jobId);
                var channel = scope.ServiceProvider.GetRequiredService<Channel<Guid>>();
                await channel.Writer.WriteAsync(jobId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume incomplete upgrade jobs");
        }
    }
}
