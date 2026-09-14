// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Server.Configuration;
using BinStash.Infrastructure.Data;
using BinStash.Server.Services.Billing;
using BinStash.Server.Services.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BinStash.Server.HostedServices;

public sealed class TenantStorageStatsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantStorageStatsHostedService> _logger;

    public TenantStorageStatsHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TenantStorageStatsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _configuration.GetValue("Billing:StorageStatsIntervalMinutes", new BillingSettings().StorageStatsIntervalMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Use CancellationToken.None so an in-progress snapshot always completes
                // atomically; stoppingToken is only used to exit the wait between runs.
                await RunOnceAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Billing: failed to record per-tenant storage stats");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var calculator = scope.ServiceProvider.GetRequiredService<ITenantFootprintCalculator>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITenantFootprintPublisher>();

        var tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // The walk is the only thing that establishes the figure. TenantUsageService and
                // TenantQuotaGuard both read the snapshot it publishes, so the meter and the
                // ceiling stay the same number.
                var snapshot = await calculator.ComputeAsync(tenantId, cancellationToken);
                await publisher.PublishAsync(snapshot, cancellationToken);
            }
            catch (TenantFootprintUnavailableException ex)
            {
                // One tenant with an unreadable release must not stop the rest from being
                // measured, and must not overwrite their own last good figure with a low one.
                _logger.LogWarning(
                    "Billing: keeping the previous storage snapshot for tenant {TenantId}: {Reason}",
                    tenantId, ex.Message);
            }
        }
    }
}
