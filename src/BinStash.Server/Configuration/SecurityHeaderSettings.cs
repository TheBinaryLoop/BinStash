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
/// Response security headers. Bind to the <c>SecurityHeaders</c> section.
/// </summary>
public sealed class SecurityHeaderSettings
{
    public const string SectionName = "SecurityHeaders";

    /// <summary>Master switch for all headers below. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The <c>Content-Security-Policy</c> value, or empty to send none.
    /// </summary>
    /// <remarks>
    /// The default permits inline script, which is weaker than it looks but is what the shipped
    /// SPA needs: <c>index.html</c> resolves the colour theme in an inline script before first
    /// paint, deliberately, so a reload does not flash the wrong theme. Tightening this to
    /// <c>script-src 'self' 'sha256-…'</c> means pinning that script's hash and updating it
    /// whenever the script changes — worth doing, but it belongs with a frontend build step that
    /// emits the hash rather than a constant hand-maintained here.
    ///
    /// <para>
    /// The directives that do carry their weight regardless are <c>frame-ancestors</c>,
    /// <c>object-src</c>, <c>base-uri</c> and <c>form-action</c>: they close off clickjacking,
    /// plugin embedding, base-tag injection and form-based exfiltration, none of which
    /// <c>'unsafe-inline'</c> undermines.
    /// </para>
    ///
    /// <para>
    /// <c>connect-src 'self'</c> covers the GraphQL WebSocket: a same-origin <c>wss:</c> endpoint
    /// matches <c>'self'</c>. A deployment that serves the API and the console from different
    /// origins has to widen this.
    /// </para>
    /// </remarks>
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary><c>Referrer-Policy</c>. Empty to send none.</summary>
    public string ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary><c>X-Frame-Options</c>. Redundant with CSP's <c>frame-ancestors</c> for current
    /// browsers, kept for older ones. Empty to send none.</summary>
    public string FrameOptions { get; set; } = "DENY";

    /// <summary><c>Cross-Origin-Opener-Policy</c>. Empty to send none.</summary>
    public string CrossOriginOpenerPolicy { get; set; } = "same-origin";

    /// <summary>
    /// Whether to send <c>X-Content-Type-Options: nosniff</c>. Defaults to <c>true</c> and there
    /// is no good reason to turn it off.
    /// </summary>
    public bool ContentTypeOptionsNoSniff { get; set; } = true;

    /// <summary>
    /// HSTS max-age in days, applied outside Development. Defaults to 365.
    /// </summary>
    /// <remarks>
    /// HSTS is close to irreversible for the lifetime of the max-age — a browser that has seen
    /// the header will refuse plain HTTP to this host until it expires. That is the point, but it
    /// means an instance served over HTTP on purpose (behind a TLS-terminating proxy on a private
    /// network, say) must set <see cref="HstsEnabled"/> to false rather than discover the
    /// consequences later.
    /// </remarks>
    public int HstsMaxAgeDays { get; set; } = 365;

    public bool HstsEnabled { get; set; } = true;

    /// <summary>Whether to add <c>includeSubDomains</c> to the HSTS header.</summary>
    public bool HstsIncludeSubDomains { get; set; } = true;
}
