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

using System.Diagnostics;
using BinStash.Server.Configuration;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Middlewares;

/// <summary>
/// Logs structured request metrics after each HTTP request completes.
///
/// <list type="bullet">
///   <item>Non-successful (4xx/5xx) responses — logged as <c>Warning</c>
///   unless the status code is in <see cref="RequestMetricsSettings.ExpectedNonSuccessStatusCodes"/>,
///   in which case it is logged at <c>Information</c>.</item>
///   <item>Slow requests — logged as <c>Warning</c> when duration exceeds
///   <see cref="RequestMetricsSettings.SlowRequestThreshold"/>.</item>
///   <item>Unhandled exceptions — logged as <c>Error</c> before the
///   exception is re-thrown so upstream error handlers can still respond.</item>
///   <item>All other requests — logged at <c>Information</c> only when
///   <see cref="RequestMetricsSettings.LogAllRequests"/> is <c>true</c>.</item>
/// </list>
/// </summary>
public sealed class RequestMetricsMiddleware(
    RequestDelegate next,
    ILogger<RequestMetricsMiddleware> logger,
    IOptionsMonitor<RequestMetricsSettings> opts)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var settings = opts.CurrentValue;

        if (!settings.Enabled)
        {
            await next(ctx);
            return;
        }

        var sw = Stopwatch.StartNew();
        var requestId = ctx.TraceIdentifier;
        var method = ctx.Request.Method;
        var path = ctx.Request.Path;
        var query = ctx.Request.QueryString;

        try
        {
            await next(ctx);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            logger.LogError(ex,
                "Unhandled exception during {Method} {Path}{Query} — {ElapsedMs} ms [{RequestId}]",
                method, path, query, sw.ElapsedMilliseconds, requestId);
            throw;
        }
        finally
        {
            sw.Stop();
        }

        var statusCode = ctx.Response.StatusCode;
        var elapsed = sw.ElapsedMilliseconds;
        var isSlow = settings.SlowRequestThreshold.HasValue &&
                     sw.Elapsed > settings.SlowRequestThreshold.Value;
        var isSuccess = statusCode < 400;
        var isExpectedNonSuccess = !isSuccess && settings.ExpectedNonSuccessStatusCodes.Contains(statusCode);
        var isUnexpectedNonSuccess = !isSuccess && !isExpectedNonSuccess;
        
        if (isUnexpectedNonSuccess && isSlow)
        {
            logger.LogWarning(
                "Request {Method} {Path}{Query} -> {StatusCode} in {ElapsedMs} ms (threshold: {ThresholdMs} ms) [{RequestId}]",
                method, path, query, statusCode, elapsed, (long)settings.SlowRequestThreshold!.Value.TotalMilliseconds, requestId);
        }
        else if (isUnexpectedNonSuccess)
        {
            logger.LogWarning(
                "Request {Method} {Path}{Query} -> {StatusCode} in {ElapsedMs} ms [{RequestId}]",
                method, path, query, statusCode, elapsed, requestId);
        }
        else if (isSlow)
        {
            logger.LogWarning(
                "Slow request {Method} {Path}{Query} -> {StatusCode} in {ElapsedMs} ms (threshold: {ThresholdMs} ms) [{RequestId}]",
                method, path, query, statusCode, elapsed,
                (long)settings.SlowRequestThreshold!.Value.TotalMilliseconds, requestId);
        }
        else if (settings.LogAllRequests || isExpectedNonSuccess)
        {
            logger.LogInformation(
                "Request {Method} {Path}{Query} -> {StatusCode} in {ElapsedMs} ms [{RequestId}]",
                method, path, query, statusCode, elapsed, requestId);
        }
    }
}
