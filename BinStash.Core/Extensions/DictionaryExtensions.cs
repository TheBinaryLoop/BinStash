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

using System.Text.Json;

namespace BinStash.Core.Extensions;

public static class DictionaryExtensions
{
    public static string ToJson(this Dictionary<string, string> dict)
    {
        var root = new Dictionary<string, object>();

        foreach (var kvp in dict)
        {
            AddToNestedDictionary(root, kvp.Key.Split(':'), kvp.Value);
        }

        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static void AddToNestedDictionary(Dictionary<string, object> current, string[] keys, string value, int index = 0)
    {
        while (true)
        {
            var key = keys[index];

            if (index == keys.Length - 1)
            {
                current[key] = value;
                return;
            }

            if (!current.TryGetValue(key, out var existing) || existing is not Dictionary<string, object> nested)
            {
                // Overwrite any scalar value with a new dictionary
                nested = new Dictionary<string, object>();
                current[key] = nested;
            }

            current = nested;
            index += 1;
        }
    }
}