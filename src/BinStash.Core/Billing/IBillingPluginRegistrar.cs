// Copyright (C) Lukas Eßmann — AGPLv3

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BinStash.Core.Billing;

public interface IBillingPluginRegistrar
{
    void Register(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder app);
}
