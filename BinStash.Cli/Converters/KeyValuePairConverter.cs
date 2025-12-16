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

using CliFx.Extensibility;

namespace BinStash.Cli.Converters;

public class KeyValuePairConverter<TKey, TValue> : BindingConverter<KeyValuePair<TKey, TValue>>
    where TKey : notnull
    where TValue : notnull
{
    public override KeyValuePair<TKey, TValue> Convert(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            throw new ArgumentException("Value cannot be null or empty.", nameof(rawValue));

        var parts = rawValue.Split(['='], 2);
        if (parts.Length != 2)
            throw new FormatException($"Invalid key-value pair format: '{rawValue}'. Expected format is 'key=value'.");

        var key = (TKey)System.Convert.ChangeType(parts[0].Trim(), typeof(TKey));
        var value = (TValue)System.Convert.ChangeType(parts[1].Trim(), typeof(TValue));

        return new KeyValuePair<TKey, TValue>(key, value);
    }
}