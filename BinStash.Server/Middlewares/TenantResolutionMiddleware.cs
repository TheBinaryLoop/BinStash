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
using BinStash.Server.Context;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Middlewares;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, BinStashDbContext db, TenantContext tenantContext, IOptions<TenancyOptions> tenancyOpts)
    {
        var path = http.Request.Path;

        // Skip tenant resolution for tooling and public endpoints
        if (path.StartsWithSegments("/api/setup", StringComparison.OrdinalIgnoreCase) || 
             path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(http);
            return;
        }

        var tenancyOptions = tenancyOpts.Value;
        if (tenancyOptions.Mode == TenancyMode.Single)
        {
            tenantContext.TenantId = tenancyOptions.SingleTenant.TenantId;
            tenantContext.TenantSlug = tenancyOptions.SingleTenant.Slug;
            tenantContext.IsResolved = true;
            await next(http);
            return;
        }
        
        // Try to resolve tenant from route
        if (http.Request.RouteValues.TryGetGuidValue("tenantId", out var tenantId))
        {
            var tenant = await db.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => new { t.Id, t.Slug })
                .SingleOrDefaultAsync();
            if (tenant is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsync("Unknown tenant.");
                return;
            }

            tenantContext.TenantId = tenant.Id;
            tenantContext.TenantSlug = tenant.Slug;
            tenantContext.IsResolved = true;
            await next(http);
            return;
        }
        
        // Try to resolve tenant from a query parameter
        if (http.Request.Query.Count > 0 && http.Request.Query.TryGetValue("tenantId", out var tenantIdQuery) && Guid.TryParse(tenantIdQuery, out tenantId))
        {
            var tenant = await db.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => new { t.Id, t.Slug })
                .SingleOrDefaultAsync();
            if (tenant is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsync("Unknown tenant.");
                return;
            }

            tenantContext.TenantId = tenant.Id;
            tenantContext.TenantSlug = tenant.Slug;
            tenantContext.IsResolved = true;
            await next(http);
            return;
        }
        
        // Try to resolve tenant from host
        // Example host: "acme.api.tld" (or "acme.api.tld:443")
        var slug = ExtractTenantSlug(http.Request.Host.Host, tenancyOptions.DomainSuffix);
        if (slug is not null)
        {
            var tenant = await db.Tenants
                .Where(t => t.Slug == slug)
                .Select(t => new { t.Id, t.Slug })
                .SingleOrDefaultAsync();

            if (tenant is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsync("Unknown tenant.");
                return;
            }

            tenantContext.TenantId = tenant.Id;
            tenantContext.TenantSlug = tenant.Slug;
            tenantContext.IsResolved = true;

            await next(http);
            return;
        }
        
        // allow non-tenant hosts (e.g. "api.tld" for landing/health)
        await next(http);
    }

    private static string? ExtractTenantSlug(string host, string? hostSuffix)
    {
        if (string.IsNullOrWhiteSpace(hostSuffix))
            return null;

        // host == "acme.api.tld" => slug "acme"
        if (!host.EndsWith(hostSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var sub = host[..^hostSuffix.Length]; // "acme"
        if (string.IsNullOrWhiteSpace(sub))
            return null;

        // Do we need support for nested subdomains? decide rule (e.g. take first label only).
        // "foo.bar.api.tld" -> maybe "foo.bar" or "foo"
        return sub;
    }
}