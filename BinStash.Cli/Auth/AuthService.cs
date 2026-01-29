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

using System.Net.Http.Json;

namespace BinStash.Cli.Auth;

public class AuthService(HttpClient http)
{
    private record TokenResponse(string AccessToken, string RefreshToken, long ExpiresIn);

    public async Task<TokenInfo> LoginAsync(string email, string password)
    {
        var now = DateTimeOffset.UtcNow;
        var response = await http.PostAsJsonAsync("auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        var tokenInfo = new TokenInfo
        {
            AccessToken = token!.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAt = now.AddSeconds(token.ExpiresIn).UtcDateTime
        };
        await CredentialStore.SaveAsync(http.BaseAddress!, tokenInfo);
        return tokenInfo;
    }

    public void Logout()
    {
        CredentialStore.ClearAll();
    }

    public async Task<string> GetValidAccessTokenAsync()
    {
        var token = await CredentialStore.LoadAsync(http.BaseAddress!);
        if (token == null)
            throw new InvalidOperationException("Not logged in.");

        if (!token.IsExpired())
            return token.AccessToken;

        // Try refresh
        var refreshed = await RefreshAsync(token.RefreshToken);
        await CredentialStore.SaveAsync(http.BaseAddress!, refreshed);
        return refreshed.AccessToken;
    }

    private async Task<TokenInfo> RefreshAsync(string refreshToken)
    {
        var now = DateTimeOffset.UtcNow;

        var response = await http.PostAsJsonAsync("auth/refresh", new { refreshToken });
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return new TokenInfo
        {
            AccessToken = token!.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAt = now.AddSeconds(token.ExpiresIn).UtcDateTime
        };
    }
}