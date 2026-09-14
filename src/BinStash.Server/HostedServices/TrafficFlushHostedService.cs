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

using BinStash.Core.Traffic;
using BinStash.Server.Configuration;
using Microsoft.Extensions.Options;

namespace BinStash.Server.HostedServices;

/// <summary>
/// Drains buffered traffic to the database on a timer, and folds and prunes the series.
/// </summary>
/// <remarks>
/// Safe to run on every replica. The accumulate is additive and keyed on the bucket, so two
/// replicas flushing the same hour add to it rather than overwrite each other; the fold and prune
/// are idempotent set operations over rows that are then deleted. That is deliberate — traffic
/// recording should not be one more thing that needs a leader before the instance can scale out.
/// </remarks>
public sealed class TrafficFlushHostedService : BackgroundService
{
    private readonly BufferedTrafficRecorder _recorder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<TrafficSettings> _settings;
    private readonly ILogger<TrafficFlushHostedService> _logger;

    private DateTimeOffset _lastMaintenanceUtc = DateTimeOffset.MinValue;

    public TrafficFlushHostedService(BufferedTrafficRecorder recorder, IServiceScopeFactory scopeFactory, IOptionsMonitor<TrafficSettings> settings, ILogger<TrafficFlushHostedService> logger)
    {
        _recorder = recorder;
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _settings.CurrentValue.FlushInterval;
        if (interval <= TimeSpan.Zero)
            interval = TimeSpan.FromSeconds(60);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // CancellationToken.None: a flush that has already read the buffer must finish, or the
            // samples it drained are lost for nothing.
            await FlushAsync(CancellationToken.None);
            await MaintainAsync(CancellationToken.None);
        }

        // Shutting down cleanly is the one case where the loss window can be closed entirely.
        await FlushAsync(CancellationToken.None);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        try
        {
            if (!_settings.CurrentValue.Enabled)
                return;

            var deltas = _recorder.Drain();
            if (deltas.Count == 0)
                return;

            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ITrafficStore>();

            await store.AccumulateAsync(deltas, ct);

            _logger.LogDebug("Flushed {BucketCount} traffic bucket(s)", deltas.Count);
        }
        catch (Exception ex)
        {
            // The drained samples are already gone from the buffer, so this loses them. Recording
            // traffic is not worth retry machinery that could itself wedge the loop.
            _logger.LogWarning(ex, "Failed to flush traffic samples");
        }
    }

    private async Task MaintainAsync(CancellationToken ct)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastMaintenanceUtc < settings.MaintenanceInterval)
            return;

        _lastMaintenanceUtc = now;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ITrafficStore>();

            await store.RollUpHourlyAsync(now - settings.HourlyRetention, ct);
            await store.PruneDailyAsync(now - settings.DailyRetention, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to roll up or prune traffic samples");
        }
    }
}
