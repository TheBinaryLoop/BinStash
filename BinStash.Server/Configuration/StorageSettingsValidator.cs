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

namespace BinStash.Server.Configuration;

/// <summary>
/// Validates <see cref="StorageSettings"/> at startup via the options
/// validation pipeline (<c>ValidateOnStart</c>).
///
/// <list type="bullet">
///   <item>Non-Development: aborts startup if <see cref="StorageSettings.AllowedRootPath"/>
///     is absent, empty, relative, or a UNC path.</item>
///   <item>Development: emits a console warning when the value is absent so
///     developers are reminded without blocking startup.</item>
/// </list>
/// </summary>
internal sealed class StorageSettingsValidator(IHostEnvironment env, ILogger<StorageSettingsValidator> logger)
    : IValidateOptions<StorageSettings>
{
    public ValidateOptionsResult Validate(string? name, StorageSettings options)
    {
        var root = options.AllowedRootPath;

        if (string.IsNullOrWhiteSpace(root))
        {
            if (!env.IsDevelopment())
            {
                return ValidateOptionsResult.Fail(
                    "Storage:AllowedRootPath is not configured. " +
                    "Set an absolute, non-UNC path to restrict where local chunk store " +
                    "directories may be created. This setting is required in non-Development environments.");
            }

            logger.LogWarning(
                "SECURITY WARNING: Storage:AllowedRootPath is not set. " +
                "Local chunk store paths will not be validated against an allowed root. " +
                "Set Storage:AllowedRootPath before deploying to a non-Development environment.");

            return ValidateOptionsResult.Success;
        }

        // Reject UNC paths (\\server\share)
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || root.StartsWith("//", StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:AllowedRootPath '{root}' is a UNC path, which is not permitted. " +
                "Use a local absolute path.");
        }

        // Reject relative paths
        if (!System.IO.Path.IsPathRooted(root))
        {
            return ValidateOptionsResult.Fail(
                $"Storage:AllowedRootPath '{root}' is a relative path. " +
                "An absolute path is required.");
        }

        return ValidateOptionsResult.Success;
    }
}
