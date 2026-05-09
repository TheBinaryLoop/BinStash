// Copyright (C) Lukas Eßmann — AGPLv3 or later

namespace BinStash.Core.Billing.NoOp;

public sealed class NoOpBillingProvider : IBillingProvider
{
    public Task<IBillingLimits> GetLimitsAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IBillingLimits>(NoOpBillingLimits.Instance);
}
