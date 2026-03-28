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

using BinStash.Contracts.Release;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Execution;

public sealed class InputDiscoveryService : IInputDiscoveryService
{
    public IReadOnlyList<InputItem> DiscoverFiles(string rootFolder, IReadOnlyDictionary<string, Component> componentMap)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
            throw new ArgumentException("Root folder must not be empty.", nameof(rootFolder));

        if (!Directory.Exists(rootFolder))
            throw new DirectoryNotFoundException($"Root folder '{rootFolder}' does not exist.");

        var results = new List<InputItem>();

        foreach (var kvp in componentMap)
        {
            var componentFolderKey = NormalizeFolderKey(kvp.Key);
            var component = kvp.Value;

            var absoluteComponentFolder = Path.IsPathRooted(componentFolderKey)
                ? componentFolderKey
                : Path.Combine(rootFolder, componentFolderKey);

            if (!Directory.Exists(absoluteComponentFolder))
                continue;

            foreach (var file in Directory.EnumerateFiles(absoluteComponentFolder, "*.*", SearchOption.AllDirectories))
            {
                if (IsVersionControlPath(file))
                    continue;

                var info = new FileInfo(file);
                var relativeToRoot = Path.GetRelativePath(rootFolder, file);
                var relativeToComponent = Path.GetRelativePath(absoluteComponentFolder, file);

                results.Add(new InputItem(
                    AbsolutePath: file,
                    RelativePath: NormalizePath(relativeToRoot),
                    RelativePathWithinComponent: NormalizePath(relativeToComponent),
                    Component: component,
                    Length: info.Length,
                    LastWriteTimeUtc: info.LastWriteTimeUtc));
            }
        }

        return results;
    }

    private static string NormalizeFolderKey(string value)
    {
        return value
            .Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static string NormalizePath(string value)
    {
        return value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static bool IsVersionControlPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}.svn{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}.hg{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}