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

namespace BinStash.Core.Serialization;

/// <summary>
/// Metrics produced by <see cref="ReleasePackageSerializer"/> during a single
/// serialization or deserialization pass over a <c>.rdef</c> release definition file.
/// </summary>
public sealed class ReleaseDefinitionMetrics
{
    /// <summary>Total size of the <c>.rdef</c> byte stream (wire bytes).</summary>
    public long TotalBytes { get; init; }

    /// <summary>Wire format version (1–6).</summary>
    public int FormatVersion { get; init; }

    /// <summary>Number of unique content hashes referenced (§0x02).</summary>
    public int UniqueFileCount { get; init; }

    /// <summary>Number of output artifacts (§0x05).</summary>
    public int ArtifactCount { get; init; }

    /// <summary>Number of path-segment tokens in the string table (§0x03).</summary>
    public int TokenCount { get; init; }

    /// <summary>Number of custom properties (§0x04).</summary>
    public int CustomPropertyCount { get; init; }

    /// <summary>Elapsed wall-clock time for the operation.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Returns a compact single-line summary suitable for debug logging.
    /// Example: <c>v6  artifacts=1234  files=987  tokens=456  props=3  size=48.2 KB  elapsed=12ms</c>
    /// </summary>
    public override string ToString()
        => $"v{FormatVersion}  artifacts={ArtifactCount}  files={UniqueFileCount}  " +
           $"tokens={TokenCount}  props={CustomPropertyCount}  " +
           $"size={FormatSize(TotalBytes)}  elapsed={Elapsed.TotalMilliseconds:F0}ms";

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024        => $"{bytes / 1024.0:F1} KB",
        _              => $"{bytes} B"
    };
}
