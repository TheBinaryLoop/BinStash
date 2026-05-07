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

namespace BinStash.Cli.Infrastructure.Svn;

public sealed class SvnComponentMapper
{
    private readonly List<(string Prefix, string Component)> _mappings = [];

    private SvnComponentMapper()
    {
    }

    public static SvnComponentMapper Load(string? componentMapFile)
    {
        var mapper = new SvnComponentMapper();

        if (string.IsNullOrWhiteSpace(componentMapFile))
            return mapper;

        if (!File.Exists(componentMapFile))
            throw new FileNotFoundException("Component map file not found.", componentMapFile);

        foreach (var rawLine in File.ReadAllLines(componentMapFile))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(':', 2);
            if (parts.Length != 2)
                throw new InvalidOperationException($"Invalid component map line: {line}");

            var prefix = Normalize(parts[0]);
            var component = parts[1].Trim();

            mapper._mappings.Add((prefix, component));
        }

        mapper._mappings.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        return mapper;
    }

    public string ResolveComponent(string relativePath)
    {
        var normalized = Normalize(relativePath);

        foreach (var mapping in _mappings)
        {
            if (normalized.StartsWith(mapping.Prefix, StringComparison.OrdinalIgnoreCase))
                return mapping.Component;
        }

        // Default behavior: first path segment becomes component, otherwise "default".
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[0] : "default";
    }

    public string ResolveReleaseFileName(string relativePath, string component)
    {
        var normalized = Normalize(relativePath);
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (component.Equals("default", StringComparison.OrdinalIgnoreCase))
            return normalized;

        if (parts.Length > 1 && parts[0].Equals(component, StringComparison.OrdinalIgnoreCase))
            return string.Join('/', parts.Skip(1));

        return normalized;
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}