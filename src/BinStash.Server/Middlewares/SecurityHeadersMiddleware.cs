// Copyright (C) 2025-2026  Lukas Eßmann
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Server.Configuration;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Middlewares;

/// <summary>
/// Adds the response security headers described by <see cref="SecurityHeaderSettings"/>.
/// </summary>
/// <remarks>
/// Registered early so the headers reach every response — including static SPA assets, error
/// pages, and responses short-circuited by the gates and the rate limiter further down. Headers
/// are written from <c>OnStarting</c> because most of those responses never return through this
/// middleware.
///
/// <para>
/// Existing values are never overwritten. An endpoint that has deliberately set its own policy
/// knows something this middleware does not.
/// </para>
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptionsMonitor<SecurityHeaderSettings> options)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
            return next(ctx);

        ctx.Response.OnStarting(static state =>
        {
            var (context, config) = ((HttpContext, SecurityHeaderSettings))state;
            var headers = context.Response.Headers;

            if (config.ContentTypeOptionsNoSniff)
                SetIfAbsent(headers, "X-Content-Type-Options", "nosniff");

            SetIfAbsent(headers, "Referrer-Policy", config.ReferrerPolicy);
            SetIfAbsent(headers, "X-Frame-Options", config.FrameOptions);
            SetIfAbsent(headers, "Cross-Origin-Opener-Policy", config.CrossOriginOpenerPolicy);

            // The OpenAPI reference UI is a dev-only surface that loads its bundle from a CDN, so
            // the SPA's policy does not describe it. The rest of the headers still apply.
            if (!IsApiReference(context.Request.Path))
                SetIfAbsent(headers, "Content-Security-Policy", config.ContentSecurityPolicy);

            return Task.CompletedTask;
        }, (ctx, settings));

        return next(ctx);
    }

    private static bool IsApiReference(PathString path)
        => path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !headers.ContainsKey(name))
            headers[name] = value;
    }
}
