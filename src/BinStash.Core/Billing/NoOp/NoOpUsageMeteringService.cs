// Copyright (C) Lukas Eßmann — AGPLv3 or later

using Microsoft.Extensions.Logging;

namespace BinStash.Core.Billing.NoOp;

public sealed class NoOpUsageMeteringService : IUsageMeteringService
{
    private readonly ILogger<NoOpUsageMeteringService> _logger;

    public NoOpUsageMeteringService(ILogger<NoOpUsageMeteringService> logger)
    {
        _logger = logger;
    }

    public void RecordIngest(Guid tenantId, long bytes)
    {
        _logger.LogDebug("No-op ingest metering for tenant {TenantId}: {Bytes} bytes", tenantId, bytes);
    }

    public void RecordEgress(Guid tenantId, long bytes)
    {
        _logger.LogDebug("No-op egress metering for tenant {TenantId}: {Bytes} bytes", tenantId, bytes);
    }

    public Task RecordStorageSnapshotAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        _logger.LogDebug("No-op storage snapshot metering for tenant {TenantId}: {Bytes} bytes", tenantId, bytes);
        return Task.CompletedTask;
    }
}
