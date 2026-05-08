// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Core.Billing.NoOp;

namespace BinStash.Server;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddNoOpBilling(this IServiceCollection services)
    {
        services.AddSingleton<IBillingProvider, NoOpBillingProvider>();
        services.AddSingleton<IUsageMeteringService, NoOpUsageMeteringService>();
        return services;
    }
}
