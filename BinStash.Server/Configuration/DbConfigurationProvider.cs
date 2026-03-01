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

using Npgsql;

namespace BinStash.Server.Configuration;

public class DbConfigurationProvider(string connectionString) : ConfigurationProvider
{
    public override void Load()
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            
            using var cmd = new NpgsqlCommand("""
                SELECT "Key", "Value"
                FROM "InstanceSettings"
            """, conn);

            using var reader = cmd.ExecuteReader();

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = reader.GetString(1);
                data[key] = value;
            }

            Data = data;
        }
        catch
        {
            // First start before migrations, or DB unavailable: treat as no overrides.
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}