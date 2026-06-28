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

namespace BinStash.Cli.Versioning;

/// <summary>
/// Thrown when the server rejects the CLI request because the CLI version is
/// below the server's configured minimum (<c>426 Upgrade Required</c>).
/// </summary>
public sealed class CliVersionIncompatibleException : Exception
{
    public string ClientVersion { get; }
    public string MinimumVersion { get; }
    public string ServerVersion { get; }

    public CliVersionIncompatibleException(
        string clientVersion,
        string minimumVersion,
        string serverVersion)
        : base(
            $"Your BinStash CLI (v{clientVersion}) is not compatible with this server. " +
            $"Please upgrade to v{minimumVersion} or later. " +
            $"Server version: {serverVersion}.")
    {
        ClientVersion = clientVersion;
        MinimumVersion = minimumVersion;
        ServerVersion = serverVersion;
    }
}
