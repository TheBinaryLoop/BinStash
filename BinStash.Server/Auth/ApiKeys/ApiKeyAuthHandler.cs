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

using System.Security.Claims;
using System.Text.Encodings.Web;
using BinStash.Core.Auth;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Auth.ApiKeys;

public class ApiKeyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, BinStashDbContext db, IPasswordHasher<ApiKey> hasher) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth) ||
            !auth.ToString().StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var value = auth.ToString()["ApiKey ".Length..].Trim();
        var parts = value.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var keyId))
            return AuthenticateResult.Fail("Invalid API key format.");

        var secret = parts[1];

        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == keyId);

        if (key is null || !key.IsActive)
            return AuthenticateResult.Fail("API key revoked/expired.");

        var verified = hasher.VerifyHashedPassword(key, key.SecretHash, secret);
        if (verified == PasswordVerificationResult.Failed)
            return AuthenticateResult.Fail("Invalid API key.");

        key.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.SubjectId.ToString()),
            new("auth_type", "machine"),
            new("subject_type", key.SubjectType.ToString()),
        };
        claims.AddRange(key.Scopes.Select(scope => new Claim("scope", scope)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
