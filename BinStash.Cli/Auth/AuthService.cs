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

using System.Net.Http.Json;

namespace BinStash.Cli.Auth;

public class AuthService
{
    private readonly HttpClient _Http;
    private readonly SecureTokenStore _Store;

    public AuthService(HttpClient http, SecureTokenStore? store = null)
    {
        _Http = http;
        _Store = store ?? new SecureTokenStore();
    }

    public async Task<TokenInfo> LoginAsync(string username, string password)
    {
        var response = await _Http.PostAsJsonAsync("/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenInfo>();
        _Store.Save(_Http.BaseAddress!.Host, token!);
        return token!;
    }

    public void Logout()
    {
        _Store.Clear(_Http.BaseAddress!.Host);
    }

    public async Task<string> GetValidAccessTokenAsync()
    {
        var token = _Store.Load(_Http.BaseAddress!.Host);
        if (token == null)
            throw new InvalidOperationException("Not logged in.");

        if (!token.IsExpired())
            return token.AccessToken;

        // Try refresh
        var refreshed = await RefreshAsync(token.RefreshToken);
        _Store.Save(_Http.BaseAddress!.Host, refreshed);
        return refreshed.AccessToken;
    }

    private async Task<TokenInfo> RefreshAsync(string refreshToken)
    {
        var response = await _Http.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TokenInfo>() ?? throw new Exception("Failed to refresh token");
    }
}