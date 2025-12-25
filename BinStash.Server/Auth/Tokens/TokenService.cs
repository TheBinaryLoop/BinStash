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

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BinStash.Core.Auth;
using BinStash.Core.Auth.Tokens;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace BinStash.Server.Auth.Tokens;

public class TokenService(
    IConfiguration configuration,
    BinStashDbContext db,
    IPasswordHasher<BinStashUser> userPasswordHasher,
    IPasswordHasher<ApiKey> apiKeyPasswordHasher)
    : ITokenService
{
    public async Task<(string accessToken, string refreshToken)> CreateTokensAsync(IdentityUser<Guid> user)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:Jwt:Key"]!));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: configuration["Auth:Jwt:Issuer"]!,
            audience: null, // not validating audience
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(15),
            signingCredentials: creds
        );

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        
        var rawRefreshToken = GenerateSecureToken();
        var tokenHash = userPasswordHasher.HashPassword((BinStashUser)user, rawRefreshToken);

        // generate refresh token
        var refreshToken = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        };

        db.UserRefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();

        return (accessToken, $"{Convert.ToHexStringLower(refreshToken.Id.ToByteArray())}.{rawRefreshToken}");
    }

    public async Task<(ApiKey apiKey, string rawApiKey)> CreateApiKeyAsync(SubjectType subjectType, Guid subjectId, string name, DateTimeOffset? requestExpiresAt)
    {
        var rawApiKey = GenerateSecureToken();
        var apiKey = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            SubjectType = subjectType,
            SubjectId = subjectId,
            DisplayName = name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (requestExpiresAt.HasValue) 
            apiKey.ExpiresAt = requestExpiresAt.Value;
        
        var apiKeyHash = apiKeyPasswordHasher.HashPassword(apiKey, rawApiKey);
        apiKey.SecretHash = apiKeyHash;
        
        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync();
        return (apiKey, rawApiKey);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexStringLower(bytes);
    }
}