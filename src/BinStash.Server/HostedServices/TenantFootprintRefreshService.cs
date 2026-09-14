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

using BinStash.Server.Services.Billing;

namespace BinStash.Server.HostedServices;

/// <summary>
/// Recomputes a single tenant's footprint shortly after their ingest finalizes, so the figure
/// their plan limit is checked against does not stay a day stale after a large upload.
/// </summary>
public sealed class TenantFootprintRefreshService : BackgroundService
{
    private readonly TenantFootprintRefreshQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantFootprintRefreshService> _logger;

    public TenantFootprintRefreshService(TenantFootprintRefreshQueue queue, IServiceScopeFactory scopeFactory, ILogger<TenantFootprintRefreshService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var tenantId in _queue.ReadAllAsync(stoppingToken))
        {
            // Freed before the walk rather than after: an ingest that finalizes while this one is
            // running must be able to queue the tenant again, or its bytes would wait for the
            // daily sweep.
            _queue.Release(tenantId);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var calculator = scope.ServiceProvider.GetRequiredService<ITenantFootprintCalculator>();
                var publisher = scope.ServiceProvider.GetRequiredService<ITenantFootprintPublisher>();

                var snapshot = await calculator.ComputeAsync(tenantId, stoppingToken);
                await publisher.PublishAsync(snapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (TenantFootprintUnavailableException ex)
            {
                // The previous snapshot stays authoritative. The daily sweep retries, and an
                // unreadable release is a problem the operator has to see regardless.
                _logger.LogWarning(
                    "Billing: keeping the previous storage snapshot for tenant {TenantId}: {Reason}",
                    tenantId, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Billing: failed to refresh the storage footprint of tenant {TenantId}", tenantId);
            }
        }
    }
}
