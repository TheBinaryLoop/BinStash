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

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BinStash.Cli.Auth;

internal record AuthFile(Dictionary<string, TokenInfo> Hosts);

/// <summary>
/// Persists auth tokens to <c>%APPDATA%/BinStash/Cli/auth.dat</c>.
/// Encryption uses Windows DPAPI (<c>ProtectedData</c>) on Windows and
/// AES-256-GCM with a machine-derived key on other platforms.
/// Both paths are AOT-compatible — no XML serialization or reflection.
/// </summary>
internal static class CredentialStore
{
    // Magic prefix written at the start of every auth.dat to identify the format version.
    // Version 2 = ProtectedData/AES-GCM format (replaces v1 DataProtection XML key ring format).
    private static readonly byte[] FormatMagic = [0x42, 0x53, 0x41, 0x02]; // "BSA\x02"

    private static string GetCredFilePath()
    {
        // Windows: %APPDATA%\BinStash\Cli
        // Linux:   ~/.config/BinStash/Cli
        // macOS:   ~/Library/Application Support/BinStash/Cli
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

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static async Task<AuthFile> LoadAllInternalAsync()
    {
        var path = GetCredFilePath();
        if (!File.Exists(path))
            return EmptyAuthFile();

        byte[] fileBytes;
        try
        {
            fileBytes = await File.ReadAllBytesAsync(path);
        }
        catch
        {
            return EmptyAuthFile();
        }

        // Check for the v2 magic prefix. If it's missing the file is either
        // empty, corrupt, or in the old DataProtection (v1) format — discard it.
        if (fileBytes.Length < FormatMagic.Length ||
            !fileBytes.AsSpan(0, FormatMagic.Length).SequenceEqual(FormatMagic))
        {
            // Old format or corrupt — start fresh (forces re-login).
            return EmptyAuthFile();
        }

        var cipherPayload = fileBytes[FormatMagic.Length..];

        byte[] plaintext;
        try
        {
            plaintext = Decrypt(cipherPayload);
        }
        catch
        {
            // Decryption failure (e.g. key changed, file tampered) — start fresh.
            return EmptyAuthFile();
        }

        var json = Encoding.UTF8.GetString(plaintext);
        var authFile = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.AuthFile);
        return authFile ?? EmptyAuthFile();
    }

    private static async Task SaveAllInternalAsync(AuthFile authFile)
    {
        var json = JsonSerializer.Serialize(authFile, SourceGenerationContext.Default.AuthFile);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var cipherPayload = Encrypt(plaintext);

        // Write magic + encrypted payload atomically via a temp file.
        var path = GetCredFilePath();
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fs.WriteAsync(FormatMagic);
            await fs.WriteAsync(cipherPayload);
        }
        File.Move(tmp, path, overwrite: true);
    }

    // ── Encryption helpers ────────────────────────────────────────────────────

    private static byte[] Encrypt(byte[] plaintext)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows DPAPI — ties ciphertext to the current Windows user account.
            // ProtectedData.Protect is AOT-safe (single P/Invoke, no reflection).
            return ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        }

        return EncryptAesGcm(plaintext);
    }

    private static byte[] Decrypt(byte[] ciphertext)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
        }

        return DecryptAesGcm(ciphertext);
    }

    // ── AES-256-GCM fallback (Linux / macOS) ─────────────────────────────────
    // Wire format: [12-byte nonce][16-byte tag][ciphertext]

    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes   = 16;

    /// <summary>
    /// Derives a 32-byte AES key from stable machine + user identifiers using
    /// HKDF-SHA256.  Not as strong as DPAPI (no OS-backed key storage) but
    /// sufficient for protecting short-lived CLI auth tokens at rest.
    /// </summary>
    private static byte[] DeriveAesKey()
    {
        // Use machine name + user name as IKM; "BinStash.Cli.CredentialStore" as info.
        var ikm = Encoding.UTF8.GetBytes(
            $"{Environment.MachineName}:{Environment.UserName}");
        var info = "BinStash.Cli.CredentialStore"u8;
        var salt = "BinStash.v2.auth"u8;

        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, key, salt, info);
        return key;
    }

    private static byte[] EncryptAesGcm(byte[] plaintext)
    {
        var key   = DeriveAesKey();
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag        = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: nonce || tag || ciphertext
        var result = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        nonce.CopyTo(result.AsSpan(0));
        tag.CopyTo(result.AsSpan(NonceSizeBytes));
        ciphertext.CopyTo(result.AsSpan(NonceSizeBytes + TagSizeBytes));
        return result;
    }

    private static byte[] DecryptAesGcm(byte[] data)
    {
        if (data.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Credential data is too short to be valid.");

        var key        = DeriveAesKey();
        var nonce      = data.AsSpan(0, NonceSizeBytes);
        var tag        = data.AsSpan(NonceSizeBytes, TagSizeBytes);
        var ciphertext = data.AsSpan(NonceSizeBytes + TagSizeBytes);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AuthFile EmptyAuthFile() =>
        new(new Dictionary<string, TokenInfo>(StringComparer.OrdinalIgnoreCase));

    private static string NormalizeHost(Uri baseUri) =>
        baseUri.GetLeftPart(UriPartial.Authority); // e.g. "https://api.example.com:5001"
}
