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

using BinStash.Infrastructure.Data;
using BinStash.Server.Configuration.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Middlewares;

public sealed class SetupGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, BinStashDbContext db, IOptions<TenancyOptions> tenancyOpts)
    {
        var path = ctx.Request.Path;

        // Allow setup + tooling endpoints
        if (path.StartsWithSegments("/api/setup", StringComparison.OrdinalIgnoreCase) || 
            path.StartsWithSegments("/api/chunkstores/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        var state = await db.SetupStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
        if (state is null || !state.IsInitialized)
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsync("setup_required");
            return;
        }

        await next(ctx);
    }
}