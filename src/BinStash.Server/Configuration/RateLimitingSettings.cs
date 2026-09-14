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

namespace BinStash.Server.Configuration;

/// <summary>
/// Request-rate ceilings for the endpoints that are cheap to call and expensive to answer.
/// Bind to the <c>RateLimiting</c> section.
/// </summary>
/// <remarks>
/// Deliberately scoped to named policies attached to specific endpoints rather than a global
/// limiter. A global limiter would also sit in front of chunk upload and release download, where
/// a legitimate CI client issues thousands of requests in a burst and throttling it would break
/// ingest rather than protect anything.
///
/// <para>
/// Limits partition on client IP. Behind a reverse proxy that means the proxy's address unless
/// forwarded headers are honoured, so a deployment that terminates TLS upstream must configure
/// <c>ForwardedHeaders</c> for these to partition per real caller.
/// </para>
/// </remarks>
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Master switch. Defaults to <c>true</c>: a public instance should be protected by default,
    /// and an operator who needs it off has to say so.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Credential-checking endpoints: login, machine token exchange, token refresh. These are the
    /// credential-stuffing surface. Identity's account lockout only counts failures against a
    /// known account, so it does nothing about an attacker spraying one password across many
    /// addresses — this does.
    /// </summary>
    public RateLimitWindow Authentication { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) };

    /// <summary>
    /// Endpoints that cause an email to be sent to an address the caller supplies: registration,
    /// password reset request, confirmation resend. Unthrottled these are a way to have the
    /// instance send mail to a third party on demand, which costs money and burns sender
    /// reputation. Deliberately tighter and over a longer window than <see cref="Authentication"/>.
    /// </summary>
    public RateLimitWindow EmailDispatch { get; set; } = new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) };

    /// <summary>
    /// Ingest session creation. Not a cost in itself, but each session is a unit of write
    /// admission, and creating them in a loop is how a client would try to work around the
    /// per-session quota check.
    /// </summary>
    public RateLimitWindow IngestSessionCreation { get; set; } = new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) };
}

/// <summary>A fixed window: at most <see cref="PermitLimit"/> requests per <see cref="Window"/>.</summary>
public sealed class RateLimitWindow
{
    public int PermitLimit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many requests may wait for a permit instead of being rejected outright. Defaults to 0:
    /// for these endpoints a fast 429 is a better answer than a held connection, and a queue is
    /// itself a resource an attacker can fill.
    /// </summary>
    public int QueueLimit { get; set; } = 0;
}
