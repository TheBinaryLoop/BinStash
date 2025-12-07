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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BinStash.Cli.Auth;

internal static class CredentialStore
{
    private record AuthFile(Dictionary<string, TokenInfo> Hosts);
    
    private static string GetCredFilePath()
    {
        // Windows: %APPDATA%\BinStash\Cli
        // Linux: ~/.config/BinStash/Cli
        // macOS: ~/Library/Application Support/BinStash/Cli
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "BinStash", "Cli");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "auth.dat");
    }
    
    public static async Task SaveAsync(Uri baseUri, TokenInfo token)
    {
        var hostKey = NormalizeHost(baseUri);
        var authFile = await LoadAllInternalAsync();
        authFile.Hosts[hostKey] = token;
        await SaveAllInternalAsync(authFile);
    }
    
    public static async Task<TokenInfo?> LoadAsync(Uri baseUri)
    {
        var hostKey = NormalizeHost(baseUri);
        var authFile = await LoadAllInternalAsync();

        return authFile.Hosts.GetValueOrDefault(hostKey);
    }
    
    public static async Task RemoveAsync(Uri baseUri)
    {
        var hostKey = NormalizeHost(baseUri);
        var authFile = await LoadAllInternalAsync();
        if (authFile.Hosts.Remove(hostKey))
        {
            await SaveAllInternalAsync(authFile);
        }
    }

    public static void ClearAll()
    {
        var path = GetCredFilePath();
        if (File.Exists(path))
            File.Delete(path);
    }

    public static async Task<IReadOnlyDictionary<string, TokenInfo>> ListHostsAsync()
    {
        var authFile = await LoadAllInternalAsync();
        return authFile.Hosts;
    }
    
    
    private static async Task<AuthFile> LoadAllInternalAsync()
    {
        var path = GetCredFilePath();
        if (!File.Exists(path))
            return new AuthFile(new Dictionary<string, TokenInfo>(StringComparer.OrdinalIgnoreCase));

        var protectedBytes = await File.ReadAllBytesAsync(path);
        var plaintext = ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        var json = Encoding.UTF8.GetString(plaintext);

        var authFile = JsonSerializer.Deserialize<AuthFile>(json);
        return authFile ?? new AuthFile(new Dictionary<string, TokenInfo>(StringComparer.OrdinalIgnoreCase));
    }
    
    private static async Task SaveAllInternalAsync(AuthFile authFile)
    {
        var json = JsonSerializer.Serialize(authFile, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        var plaintext = Encoding.UTF8.GetBytes(json);

        var protectedBytes = ProtectedData.Protect(
            plaintext,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(GetCredFilePath(), protectedBytes);
    }
    
    private static string NormalizeHost(Uri baseUri)
    {
        // How fine-grained do we want this to be?
        // - Scheme + host + port (Authority)
        // - Or just host
        //
        // Docker uses registry URLs as keys; this is similar. Does this fit for this use case?
        return baseUri.GetLeftPart(UriPartial.Authority); // e.g. "https://api.example.com:5001"
    }
}