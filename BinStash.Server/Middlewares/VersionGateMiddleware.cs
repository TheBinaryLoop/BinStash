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

using System.Reflection;
using BinStash.Server.Configuration;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Middlewares;

/// <summary>
/// Rejects CLI requests whose reported version is below the configured
/// <see cref="VersionGateSettings.MinimumCliVersion"/>.
///
/// The CLI announces its version via the <c>X-BinStash-Cli-Version</c>
/// request header.  Requests without this header — e.g. browser / API
/// clients — are passed through unchanged.
///
/// Incompatible CLI requests receive a structured
/// <c>426 Upgrade Required</c> problem response so the CLI can surface a
/// clear human-readable message.
/// </summary>
public sealed class VersionGateMiddleware(RequestDelegate next)
{
    /// <summary>The header name the CLI sends its version in.</summary>
    private const string CliVersionHeader = "X-BinStash-Cli-Version";

    /// <summary>
    /// The server version, advertised back to the client in the
    /// <c>X-BinStash-Server-Version</c> response header.
    /// </summary>
    private static readonly string ServerVersion =
        typeof(VersionGateMiddleware).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0]
        ?? "unknown";

    public async Task InvokeAsync(HttpContext ctx, IOptions<VersionGateSettings> opts)
    {
        // Always advertise the server version so the CLI can display it.
        ctx.Response.Headers["X-BinStash-Server-Version"] = ServerVersion;

        var minimumRaw = opts.Value.MinimumCliVersion;

        // Gate disabled — nothing to check.
        if (string.IsNullOrWhiteSpace(minimumRaw))
        {
            await next(ctx);
            return;
        }

        // No CLI version header — not a CLI request; let it through.
        if (!ctx.Request.Headers.TryGetValue(CliVersionHeader, out var clientVersionRaw) ||
            string.IsNullOrWhiteSpace(clientVersionRaw))
        {
            await next(ctx);
            return;
        }

        // Parse both versions; on any parse failure, pass through rather than
        // accidentally blocking valid requests.
        if (!Version.TryParse(minimumRaw, out var minimumVersion))
        {
            await next(ctx);
            return;
        }

        if (!Version.TryParse(clientVersionRaw.ToString(), out var clientVersion))
        {
            await next(ctx);
            return;
        }

        if (clientVersion >= minimumVersion)
        {
            await next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://binstash.io/errors/cli-upgrade-required",
            title = "CLI Upgrade Required",
            status = 426,
            detail =
                $"Your BinStash CLI version ({clientVersion}) is not compatible with this server. " +
                $"Please upgrade to version {minimumVersion} or later.",
            errorCode = "cli_upgrade_required",
            clientVersion = clientVersion.ToString(),
            minimumVersion = minimumVersion.ToString(),
            serverVersion = ServerVersion
        });
    }
}
