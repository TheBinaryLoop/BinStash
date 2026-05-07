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

namespace BinStash.Cli.Services.Releases;

public sealed class ComponentMapLoader
{
    public Dictionary<string, Component> Load(string? mapFile, string rootFolder, Action<string>? log = null)
    {
        var dict = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(mapFile))
        {
            var lines = File.ReadAllLines(mapFile);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                {
                    var folder = parts[0].Trim();
                    var name = parts[1].Trim();

                    if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
                    {
                        log?.Invoke($"Invalid component map line: {rawLine}. Expected format: <ComponentFolder>:<ComponentName>");
                        continue;
                    }

                    var normalizedFolder = NormalizeFolderKey(folder);

                    var comp = new Component
                    {
                        Name = name,
                        Files = new()
                    };

                    dict[normalizedFolder] = comp;
                }
                else
                {
                    log?.Invoke($"Invalid component map line: {rawLine}. Expected format: <ComponentFolder>:<ComponentName>");
                }
            }
        }
        else
        {
            log?.Invoke("No component map file provided. Using folder names as components.");

            foreach (var dir in Directory.GetDirectories(rootFolder).Where(x => !IsVersionControlPath(x)))
            {
                var name = Path.GetFileName(dir);
                var relative = Path.GetRelativePath(rootFolder, dir);
                var normalizedFolder = NormalizeFolderKey(relative);

                var comp = new Component
                {
                    Name = name,
                    Files = new()
                };

                dict[normalizedFolder] = comp;
            }
        }

        return dict;
    }

    private static string NormalizeFolderKey(string value)
    {
        return value
            .Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static bool IsVersionControlPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}.svn{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}.hg{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith($"{Path.DirectorySeparatorChar}.git", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith($"{Path.DirectorySeparatorChar}.svn", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith($"{Path.DirectorySeparatorChar}.hg", StringComparison.OrdinalIgnoreCase);
    }
}