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

using System.Text.Json;
using KeySharp;

namespace BinStash.Cli.Auth;

public class SecureTokenStore
{
    public TokenInfo? Load(string service)
    {
        var json = Keyring.GetPassword("BinStash", service, Environment.UserName);
        return json == null ? null : JsonSerializer.Deserialize<TokenInfo>(json);
    }
    
    public void Save(string service, TokenInfo token)
    {
        var json = JsonSerializer.Serialize(token);
        Keyring.SetPassword("BinStash", service, Environment.UserName, json);
    }

    public void Clear(string service)
    {
        Keyring.DeletePassword("BinStash", service, Environment.UserName);
    }
}