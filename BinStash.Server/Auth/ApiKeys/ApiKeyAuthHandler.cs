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

public class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BinStashDbContext db,
    IPasswordHasher<ApiKey> hasher)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
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

        var key = await db.ApiKeys
            .SingleOrDefaultAsync(k => k.Id == keyId);
        
        if (key is null || !key.IsActive)
            return AuthenticateResult.Fail("API key revoked/expired.");

        return AuthenticateResult.Fail("Not implemented.");
        // TODO: Implement api keys for users and service accounts
        
        var subject = key.SubjectType switch
        {
            SubjectType.ServiceAccount => "service_account",
            _ => "unknown"
        };
        
        
        var verified = hasher.VerifyHashedPassword(key, key.SecretHash, secret);
        if (verified == PasswordVerificationResult.Failed)
            return AuthenticateResult.Fail("Invalid API key.");

        key.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.SubjectId.ToString()),
            new("auth_type", "machine"),
        };

        foreach (var scope in key.Scopes)
            claims.Add(new("scope", scope));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
