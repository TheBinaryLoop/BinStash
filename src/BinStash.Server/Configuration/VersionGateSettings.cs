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
/// Configuration for the CLI version compatibility gate.
/// Bind to the <c>VersionGate</c> section in <c>appsettings.json</c> or
/// environment variables.
/// </summary>
public sealed class VersionGateSettings
{
    /// <summary>
    /// The minimum CLI version that is accepted by this server.
    /// Requests from a CLI reporting a lower version are rejected with
    /// <c>426 Upgrade Required</c>.  Set to <c>null</c> or empty to disable
    /// the gate entirely.
    /// </summary>
    /// <example>1.0.0</example>
    public string? MinimumCliVersion { get; set; }
}
