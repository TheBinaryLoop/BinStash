// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace BinStash.Core.Ingestion.Formats.Zip;

public sealed class ZipMemberSelectionPolicy
{
    public long MaxEntrySizeBytes { get; init; } = 16 * 1024 * 1024;
    public bool AllowNestedZipFamily { get; init; } = false;

    public bool ShouldIngest(string entryPath, long uncompressedLength, bool isDirectory)
    {
        if (isDirectory)
            return false;

        if (string.IsNullOrWhiteSpace(entryPath))
            return false;

        if (uncompressedLength <= 0)
            return false;

        if (uncompressedLength > MaxEntrySizeBytes)
            return false;

        var normalized = entryPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        var fileName = Path.GetFileName(normalized);

        // High-value metadata and config files
        /*if (fileName is "manifest.json" or "modrinth.index.json" or "pack.mcmeta" or "plugin.yml")
            return true;*/

        var ext = Path.GetExtension(normalized);

        /*if (ext is ".json" or ".toml" or ".yaml" or ".yml" or ".xml" or ".config" or ".ini" or ".properties")
            return true;*/

        // Useful runtime artifacts
        /*if (ext is ".dll" or ".so" or ".dylib")
            return true;*/

        // Nested packages are opt-in later
        if (AllowNestedZipFamily && (ext is ".jar" or ".zip" or ".nupkg" or ".apk"))
            return true;
        
        return true;
    }
}