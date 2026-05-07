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

using Microsoft.Extensions.Options;

namespace BinStash.Server.Configuration.Auth;

/// <summary>
/// Validates <see cref="JwtSettings"/> at startup via the options validation
/// pipeline (<c>ValidateOnStart</c>).
///
/// <list type="bullet">
///   <item>Non-Development: aborts startup if the signing key is absent or equals
///     the publicly known dev fallback.</item>
///   <item>Development: emits a console warning when the dev fallback is in use
///     so developers are reminded without blocking startup.</item>
/// </list>
/// </summary>
internal sealed class JwtSettingsValidator(IHostEnvironment env, ILogger<JwtSettingsValidator> logger) : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        if (options.IsUsingDevFallback)
        {
            if (!env.IsDevelopment())
            {
                return ValidateOptionsResult.Fail(
                    "Auth:Jwt:Key is not configured or is set to the insecure development default " +
                    $"(\"{JwtSettings.DevFallbackKey}\"). " +
                    "Set a strong, random key in Auth:Jwt:Key before starting the server " +
                    "in a non-Development environment.");
            }

            logger.LogWarning(
                "SECURITY WARNING: Auth:Jwt:Key is using the insecure development fallback " +
                "\"{DevKey}\". This key is publicly visible in the repository. " +
                "Do NOT use this configuration in production.",
                JwtSettings.DevFallbackKey);
        }

        return ValidateOptionsResult.Success;
    }
}
