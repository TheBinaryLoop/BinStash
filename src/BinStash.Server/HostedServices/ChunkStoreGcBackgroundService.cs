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
using BinStash.Server.Services.ChunkStores;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.HostedServices;

/// <summary>
/// Drains the <see cref="GcJobChannel"/> queue and executes chunk-store garbage-collection jobs
/// one at a time via <see cref="IChunkStoreGcService"/>.
///
/// <para>
/// Strictly serial by design. Collection is background I/O competing with the ingest and download
/// paths for the same disks, and two runs over one store would also race each other's quarantine
/// decisions. One at a time keeps both problems away without any coordination.
/// </para>
/// <para>
/// On startup it re-enqueues jobs left <c>Pending</c> or <c>Running</c> by a previous lifecycle.
/// Restarting a run from the beginning is always safe: marking is read-only, quarantine is
/// idempotent (an object already tombstoned keeps its original clock), and reclaim re-derives
/// everything it does from the tombstones it finds.
/// </para>
/// </summary>
public sealed class ChunkStoreGcBackgroundService : BackgroundService
{
    private readonly ChannelReader<Guid> _reader;
    private readonly IServiceProvider _services;
    private readonly ILogger<ChunkStoreGcBackgroundService> _logger;

    public ChunkStoreGcBackgroundService(GcJobChannel gcJobChannel, IServiceProvider services, ILogger<ChunkStoreGcBackgroundService> logger)
    {
        _reader = gcJobChannel.Channel.Reader;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResumeIncompleteJobsAsync(stoppingToken);

        try
        {
            await foreach (var jobId in _reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("Dequeued garbage-collection job {JobId}", jobId);

                try
                {
                    using var scope = _services.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IChunkStoreGcService>();
                    await service.ExecuteAsync(jobId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error executing garbage-collection job {JobId}", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private async Task ResumeIncompleteJobsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Data.BinStashDbContext>();

            var incompleteJobIds = await db.BackgroundJobs
                .Where(j => j.JobType == BackgroundJobTypes.ChunkStoreGc
                            && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running))
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .ToListAsync(ct);

            var channel = scope.ServiceProvider.GetRequiredService<GcJobChannel>();

            foreach (var jobId in incompleteJobIds)
            {
                _logger.LogInformation("Resuming incomplete garbage-collection job {JobId} from previous lifecycle", jobId);
                await channel.Channel.Writer.WriteAsync(jobId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume incomplete garbage-collection jobs");
        }
    }
}
