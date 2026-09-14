// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Server.Configuration;
using BinStash.Server.Services.Usage;
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

        var meteringService = scope.ServiceProvider.GetRequiredService<IUsageMeteringService>();

        // One definition of "how much is this tenant storing", shared with the usage page and
        // with quota enforcement — the meter and the ceiling have to agree on the number.
        var usage = scope.ServiceProvider.GetRequiredService<TenantUsageService>();
        var tenantStats = await usage.GetLogicalBytesByTenantAsync(cancellationToken);

        foreach (var stat in tenantStats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await meteringService.RecordStorageSnapshotAsync(stat.TenantId, stat.LogicalBytes, cancellationToken);
        }
    }
}
