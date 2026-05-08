// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using Microsoft.Extensions.Caching.Memory;

namespace BinStash.Server.Billing;

public sealed class BillingLimitsCache(IBillingProvider billingProvider, IMemoryCache cache)
{
    public async Task<IBillingLimits> GetCachedLimitsAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync($"billing:limits:{tenantId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await billingProvider.GetLimitsAsync(tenantId, ct);
        }) ?? await billingProvider.GetLimitsAsync(tenantId, ct);
    }
}
