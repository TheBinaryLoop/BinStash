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
    private readonly Lock _gate = new();
    
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
    
    public override void Set(string key, string? value)
    {
        // Convention: null => delete (optional).
        if (value is null)
        {
            Remove(key);
            return;
        }

        lock (_gate)
        {
            // Persist first (so failures don't poison the in-memory state)
            UpsertToDb(key, value);

            // Update in-memory view
            Data[key] = value;
        }

        // Notify change token listeners (if anyone is watching)
        OnReload();
    }

    private void Remove(string key)
    {
        lock (_gate)
        {
            DeleteFromDb(key);
            Data.Remove(key);
        }

        OnReload();
    }

    private void UpsertToDb(string key, string value)
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand("""
                                              INSERT INTO "InstanceSettings" ("Key", "Value", "UpdatedAt")
                                              VALUES (@k, @v, NOW() AT TIME ZONE 'UTC')
                                              ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value"
                                          """, conn);

        cmd.Parameters.AddWithValue("k", key);
        cmd.Parameters.AddWithValue("v", value);

        cmd.ExecuteNonQuery();
    }

    private void DeleteFromDb(string key)
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();

        using var cmd = new NpgsqlCommand("""
                                              DELETE FROM "InstanceSettings"
                                              WHERE "Key" = @k
                                          """, conn);

        cmd.Parameters.AddWithValue("k", key);

        cmd.ExecuteNonQuery();
    }
}