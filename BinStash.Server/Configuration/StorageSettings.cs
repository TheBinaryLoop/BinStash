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
/// Configuration options for local storage security constraints.
/// Bound from the <c>Storage</c> configuration section.
/// </summary>
public sealed class StorageSettings
{
    /// <summary>
    /// The absolute filesystem path that all local chunk store paths must
    /// reside within.  When set, the server validates every <c>LocalPath</c>
    /// supplied at chunk-store creation time and rejects any path that would
    /// escape this root (including traversal sequences and UNC paths).
    ///
    /// <para>
    /// Must be an absolute, non-UNC path.  Leave empty only in Development
    /// (a warning is emitted); in all other environments startup will fail
    /// if this value is absent.
    /// </para>
    /// </summary>
    public string? AllowedRootPath { get; set; }
}
