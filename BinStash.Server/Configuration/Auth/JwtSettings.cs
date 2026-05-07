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

public sealed class JwtSettings
{
    /// <summary>
    /// The known-insecure development fallback key.  Present here so that the
    /// validator and any logging can reference the same constant rather than
    /// duplicating the string literal across files.
    /// </summary>
    public const string DevFallbackKey = "dev-only-change-me";

    /// <summary>
    /// JWT signing key.  Must be set to a strong random value in all
    /// non-Development environments.  Defaults to the publicly known dev
    /// fallback so the application can start without any explicit configuration
    /// in local development; the validator will emit a warning in that case.
    /// </summary>
    public string Key { get; set; } = DevFallbackKey;

    /// <summary>
    /// The issuer claim embedded in every JWT and validated on inbound tokens.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// True when <see cref="Key"/> was not explicitly configured and the
    /// default dev fallback is in use.  Populated by <see cref="JwtSettingsValidator"/>
    /// at startup validation time; not bound from configuration.
    /// </summary>
    public bool IsUsingDevFallback => string.IsNullOrWhiteSpace(Key) || Key == DevFallbackKey;
}
