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

using System.Text.RegularExpressions;

namespace BinStash.Cli.Infrastructure.Svn;

public sealed class SvnGlobFilter
{
    private readonly Regex[] _includes;
    private readonly Regex[] _excludes;

    public SvnGlobFilter(IEnumerable<string> includes, IEnumerable<string> excludes)
    {
        _includes = includes.Select(ToRegex).ToArray();
        _excludes = excludes.Select(ToRegex).ToArray();
    }

    public bool ShouldInclude(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');

        if (p.Contains("/.svn/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/.hg/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(".svn/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(".hg/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_includes.Length > 0 && !_includes.Any(r => r.IsMatch(p)))
            return false;

        if (_excludes.Any(r => r.IsMatch(p)))
            return false;

        return true;
    }

    private static Regex ToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob.Replace('\\', '/'))
            .Replace(@"\*\*", "___DOUBLESTAR___")
            .Replace(@"\*", "[^/]*")
            .Replace("___DOUBLESTAR___", ".*")
            .Replace(@"\?", ".") + "$";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}