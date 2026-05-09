// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Infrastructure.Data;
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
        var intervalMinutes = _configuration.GetValue<int>("Billing:StorageStatsIntervalMinutes", 60);
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
        var meteringService = scope.ServiceProvider.GetRequiredService<IUsageMeteringService>();

        var tenantStats = await db.ReleaseMetrics
            .AsNoTracking()
            .Join(
                db.Releases.AsNoTracking(),
                rm => rm.ReleaseId,
                r => r.Id,
                (rm, r) => new { rm.TotalLogicalBytes, r.RepoId })
            .Join(
                db.Repositories.AsNoTracking(),
                x => x.RepoId,
                repo => repo.Id,
                (x, repo) => new { x.TotalLogicalBytes, repo.TenantId })
            .GroupBy(x => x.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                TotalBytes = (long)g.Sum(x => (decimal)x.TotalLogicalBytes)
            })
            .ToListAsync(cancellationToken);

        foreach (var stat in tenantStats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await meteringService.RecordStorageSnapshotAsync(stat.TenantId, stat.TotalBytes, cancellationToken);
        }
    }
}
