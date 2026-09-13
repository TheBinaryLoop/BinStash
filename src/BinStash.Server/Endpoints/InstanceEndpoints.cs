// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Text.Json;
using BinStash.Core.Auth.Instance;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class InstanceEndpoints
{
    public static RouteGroupBuilder MapInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instance")!
            .WithTags("Instance");

        // Instance stats and the email/tenancy/domain configuration are served via GraphQL
        // (query instanceStats/emailConfig/tenancyConfig/domainConfig, mutation set*Config/sendTestEmail).
        // Only the anonymous public bootstrap config remains over REST.
        group.MapGet("/config", GetPublicConfig)!
            .WithName("GetPublicConfig")
            .WithDescription(
                "Gets the public configuration of the instance, such as instance name, description, etc. This should not include any sensitive information or settings that are not relevant to users.")
            .AllowAnonymous();

        return group;
    }

    private static IResult GetPublicConfig(IConfiguration config)
    {
        // TODO: Stuff like signup enabled, instance name/description, etc. that is not sensitive and can be shown to anonymous users
        return Results.Ok(new
        {
            InstanceMode = config["Instance:Mode"],
            TenancyMode = config["Tenancy:Mode"],
        });
    }
}