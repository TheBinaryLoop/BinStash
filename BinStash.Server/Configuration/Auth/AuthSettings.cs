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

namespace BinStash.Server.Configuration.Auth;

/// <summary>
/// Top-level options bound from the <c>Auth</c> configuration section.
/// </summary>
public sealed class AuthSettings
{
    /// <summary>JWT signing and validation settings (sub-section <c>Auth:Jwt</c>).</summary>
    public JwtSettings Jwt { get; set; } = new();

    /// <summary>
    /// General auth behavior settings (sub-section <c>Auth:Settings</c>).
    /// </summary>
    public AuthBehaviourSettings Settings { get; set; } = new();
}

/// <summary>
/// General authentication behavior settings bound from <c>Auth:Settings</c>.
/// </summary>
public sealed class AuthBehaviourSettings
{
    /// <summary>
    /// Whether public user registration is enabled.
    /// When <see langword="false"/> only invite-based or admin-created accounts are accepted.
    /// </summary>
    public bool AllowRegistration { get; set; }
}
