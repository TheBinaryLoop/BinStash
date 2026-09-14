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

using System.Globalization;
using System.Threading.RateLimiting;
using BinStash.Core.Auditing;
using BinStash.Core.Entities;
using BinStash.Server.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Extensions;

/// <summary>
/// Named rate-limit policy identifiers. Endpoints opt in with
/// <c>.RequireRateLimiting(RateLimitPolicies.X)</c>; nothing is limited by default.
/// </summary>
public static class RateLimitPolicies
{
    public const string Authentication = "auth";
    public const string EmailDispatch = "email-dispatch";
    public const string IngestSessionCreation = "ingest-session-create";
}

public static class RateLimitingExtensions
{
    /// <summary>
    /// Registers the named rate-limit policies described by <see cref="RateLimitingSettings"/>.
    /// </summary>
    /// <remarks>
    /// When disabled the policies are still registered, as no-ops. Endpoints reference them by
    /// name, so removing the registration would fail routing rather than lift the limit.
    /// </remarks>
    public static IServiceCollection AddBinStashRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitingSettings.SectionName).Get<RateLimitingSettings>() ?? new RateLimitingSettings();

        services.Configure<RateLimitingSettings>(configuration.GetSection(RateLimitingSettings.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddFixedWindowPolicy(options, RateLimitPolicies.Authentication, settings.Enabled, settings.Authentication);
            AddFixedWindowPolicy(options, RateLimitPolicies.EmailDispatch, settings.Enabled, settings.EmailDispatch);
            AddFixedWindowPolicy(options, RateLimitPolicies.IngestSessionCreation, settings.Enabled, settings.IngestSessionCreation);

            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;

                // Retry-After lets a well-behaved client back off instead of hammering. Only a
                // fixed-window limiter can say how long that is, hence the metadata probe.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(NumberFormatInfo.InvariantInfo);
                }

                await AuditRejectionAsync(http, ct);

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.ContentType = "application/problem+json";
                await http.Response.WriteAsJsonAsync(new
                {
                    type = "https://binstash.app/errors/rate-limited",
                    title = "Too Many Requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Too many requests from this address. Please retry later.",
                    errorCode = "rate_limited"
                }, ct);
            };
        });

        return services;
    }

    private static void AddFixedWindowPolicy(RateLimiterOptions options, string policyName, bool enabled, RateLimitWindow window)
    {
        options.AddPolicy(policyName, http =>
        {
            if (!enabled)
                return RateLimitPartition.GetNoLimiter("disabled");

            return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(http, policyName), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = window.PermitLimit,
                Window = window.Window,
                QueueLimit = window.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
        });
    }

    /// <summary>
    /// Partitions on caller address, scoped per policy so a client spending its login budget does
    /// not also lose its password-reset budget.
    /// </summary>
    private static string PartitionKey(HttpContext http, string policyName)
    {
        var address = http.Connection.RemoteIpAddress;

        // IPv6 callers routinely get a /64 each, so partitioning on the full address would let one
        // client present a practically unlimited number of partitions. Collapse to the prefix.
        var key = address is null
            ? "unknown"
            : address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? string.Join(':', address.ToString().Split(':').Take(4))
                : address.ToString();

        return $"{policyName}:{key}";
    }

    /// <summary>
    /// A throttled authentication attempt is a security event: it is what credential stuffing
    /// looks like from the inside. Other throttled endpoints are noise and are not recorded.
    /// </summary>
    private static async Task AuditRejectionAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var audit = http.RequestServices.GetService<IAuditLogWriter>();
            if (audit is null)
                return;

            await audit.WriteAsync(new AuditEntryDraft
            {
                Action = AuditActions.AuthRateLimited,
                InstanceScoped = true,
                Outcome = AuditOutcome.Denied,
                Metadata = new Dictionary<string, object?>
                {
                    ["path"] = http.Request.Path.Value
                }
            }, ct);
        }
        catch
        {
            // Never let the audit path turn a 429 into a 500.
        }
    }
}
