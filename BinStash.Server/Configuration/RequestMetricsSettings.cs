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
/// Configuration for the HTTP request metrics/logging middleware.
/// Bind to the <c>RequestMetrics</c> section in <c>appsettings.json</c> or
/// environment variables.
/// </summary>
public sealed class RequestMetricsSettings
{
    /// <summary>
    /// Whether the request metrics middleware is enabled.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Requests that take longer than this threshold will be logged as a
    /// warning regardless of their HTTP status code.
    /// Set to <c>null</c> to disable slow-request warnings.
    /// </summary>
    /// <example>00:00:05</example>
    public TimeSpan? SlowRequestThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When <c>true</c>, every completed request is logged at the
    /// <c>Information</c> level, not just slow or failed ones.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool LogAllRequests { get; set; } = false;

    /// <summary>
    /// HTTP status codes that should be treated as expected / non-alerting
    /// even though they are not 2xx.  For example, 401 and 404 may be
    /// considered normal operational traffic.  These codes are still logged
    /// but at <c>Information</c> level rather than <c>Warning</c>.
    /// </summary>
    public HashSet<int> ExpectedNonSuccessStatusCodes { get; set; } = [401, 404];
}
