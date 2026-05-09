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

using System.Reflection;

namespace BinStash.Cli.Infrastructure;

/// <summary>
/// Provides the current CLI version string derived from the assembly's
/// <see cref="AssemblyInformationalVersionAttribute"/>. Falls back to
/// <c>AssemblyVersion</c> and then to <c>"unknown"</c>.
/// </summary>
internal static class CliVersion
{
    /// <summary>
    /// The version string of the CLI assembly, e.g. <c>"1.0.0"</c> or
    /// <c>"1.2.3+gitsha"</c>. The git-SHA suffix, if present, is stripped so
    /// the server receives a clean semantic version.
    /// </summary>
    public static readonly string Value = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = typeof(CliVersion).Assembly;

        // InformationalVersion may carry a "+<commit>" suffix — strip it.
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return asm.GetName().Version?.ToString(3) ?? "unknown";
    }
}
