// Copyright (C) 2025  Lukas Eßmann
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

namespace BinStash.Core.Serialization.Utils;

internal class SubstringTableBuilder
{
    private readonly Dictionary<string, ushort> _Index = new();
    public readonly List<string> Table = new();

    public List<(ushort id, Separator sep)> Tokenize(string input)
    {
        var tokens = new List<(ushort id, Separator sep)>();
        var start = 0;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (ToSep(c) != Separator.None)
            {
                if (i > start)
                {
                    var part = input.Substring(start, i - start);
                    var id = GetOrAdd(part);
                    tokens.Add((id, ToSep(c)));
                }
                start = i + 1;
            }
        }

        if (start < input.Length)
        {
            var part = input.Substring(start);
            var id = GetOrAdd(part);
            tokens.Add((id, Separator.None));
        }

        return tokens;
    }

    private ushort GetOrAdd(string str)
    {
        if (_Index.TryGetValue(str, out var id)) return id;
        id = (ushort)Table.Count;
        Table.Add(str);
        _Index[str] = id;
        return id;
    }

    private static Separator ToSep(char s) => s switch
    {
        '.' => Separator.Dot,
        '/' => Separator.Slash,
        '\\' => Separator.Backslash,
        _ => Separator.None
    };
}

internal enum Separator : byte
{
    None = 0,
    Dot = (byte)'.',
    Slash = (byte)'/',
    Backslash = (byte)'\\',
}