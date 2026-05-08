// Copyright (C) Lukas Eßmann — AGPLv3

namespace BinStash.Core.Billing;

public interface IBillingProvider
{
    Task<IBillingLimits> GetLimitsAsync(Guid tenantId, CancellationToken ct = default);
}
