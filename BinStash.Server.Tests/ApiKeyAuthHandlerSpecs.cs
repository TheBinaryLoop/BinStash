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
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BinStash.Core.Auth;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Auth.ApiKeys;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Tests;

/// <summary>
/// Unit tests for <see cref="ApiKeyAuthHandler"/>.
/// These tests exercise key extraction, hash verification, expiry/revocation checks,
/// and claims construction without a running server or real database.
/// </summary>
public class ApiKeyAuthHandlerSpecs : IDisposable
{
    private const string SchemeName = "ApiKey";

    private readonly BinStashDbContext _db;
    private readonly PasswordHasher<ApiKey> _hasher;

    public ApiKeyAuthHandlerSpecs()
    {
        var options = new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BinStashDbContext(options);
        _hasher = new PasswordHasher<ApiKey>();
    }

    public void Dispose() => _db.Dispose();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates an <see cref="ApiKey"/> in the in-memory database, returning
    /// both the persisted entity and the raw (unhashed) secret string.
    /// </summary>
    private async Task<(ApiKey key, string rawSecret)> SeedApiKeyAsync(bool isActive = true, DateTimeOffset? expiresAt = null, DateTimeOffset? revokedAt = null, SubjectType subjectType = SubjectType.ServiceAccount, string[]? scopes = null)
    {
        var keyId = Guid.NewGuid();
        var rawSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        var key = new ApiKey
        {
            Id = keyId,
            SubjectType = subjectType,
            SubjectId = Guid.NewGuid(),
            DisplayName = "test-key",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
            Scopes = scopes ?? [],
        };
        key.SecretHash = _hasher.HashPassword(key, rawSecret);

        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync();

        return (key, rawSecret);
    }

    /// <summary>
    /// Builds a handler and initializes it against a <see cref="DefaultHttpContext"/>
    /// that carries the given Authorization header value.
    /// </summary>
    private async Task<(ApiKeyAuthHandler handler, DefaultHttpContext ctx)> BuildHandlerAsync(
        string? authorizationHeaderValue)
    {
        var loggerFactory = LoggerFactory.Create(_ => { });

        var optionsMonitor = new FakeOptionsMonitor<AuthenticationSchemeOptions>(
            new AuthenticationSchemeOptions());

        var handler = new ApiKeyAuthHandler(optionsMonitor, loggerFactory, UrlEncoder.Default, _db, _hasher);

        var scheme = new AuthenticationScheme(SchemeName, null, typeof(ApiKeyAuthHandler));

        var ctx = new DefaultHttpContext
        {
            RequestServices = BuildServiceProvider()
        };
        if (authorizationHeaderValue is not null)
            ctx.Request.Headers["Authorization"] = authorizationHeaderValue;

        await handler.InitializeAsync(scheme, ctx);
        return (handler, ctx);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();
        return services.BuildServiceProvider();
    }

    // -----------------------------------------------------------------------
    // No Authorization header → NoResult (pass-through to next scheme)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NoAuthorizationHeader_ReturnsNoResult()
    {
        var (handler, _) = await BuildHandlerAsync(null);

        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue("missing header should be a pass-through, not a failure");
    }

    // -----------------------------------------------------------------------
    // Wrong scheme prefix → NoResult
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WrongSchemePrefix_ReturnsNoResult()
    {
        var (handler, _) = await BuildHandlerAsync("Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.abc");

        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue("Bearer prefix should not be claimed by ApiKey scheme");
    }

    // -----------------------------------------------------------------------
    // Malformed key (not <id>.<secret>) → Fail
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MalformedKeyFormat_ReturnsFail()
    {
        var (handler, _) = await BuildHandlerAsync("ApiKey not-a-valid-key");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Unknown key ID → Fail
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnknownKeyId_ReturnsFail()
    {
        var unknownId = Guid.NewGuid();
        var (handler, _) = await BuildHandlerAsync($"ApiKey {unknownId}.somesecret");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("revoked/expired");
    }

    // -----------------------------------------------------------------------
    // Wrong secret → Fail
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WrongSecret_ReturnsFail()
    {
        var (key, _) = await SeedApiKeyAsync();
        var (handler, _) = await BuildHandlerAsync($"ApiKey {key.Id}.wrongsecret");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Be("Invalid API key.");
    }

    // -----------------------------------------------------------------------
    // Expired key → Fail
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExpiredKey_ReturnsFail()
    {
        var (key, rawSecret) = await SeedApiKeyAsync(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        var (handler, _) = await BuildHandlerAsync($"ApiKey {key.Id}.{rawSecret}");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("revoked/expired");
    }

    // -----------------------------------------------------------------------
    // Revoked key → Fail
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RevokedKey_ReturnsFail()
    {
        var (key, rawSecret) = await SeedApiKeyAsync(revokedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var (handler, _) = await BuildHandlerAsync($"ApiKey {key.Id}.{rawSecret}");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("revoked/expired");
    }

    // -----------------------------------------------------------------------
    // Valid key → Success, correct claims
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidKey_ReturnsSuccess_WithExpectedClaims()
    {
        var (key, rawSecret) = await SeedApiKeyAsync(
            subjectType: SubjectType.ServiceAccount,
            scopes: ["repo:read", "repo:write"]);

        var (handler, _) = await BuildHandlerAsync($"ApiKey {key.Id}.{rawSecret}");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();

        var principal = result.Principal!;
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
            .Should().Be(key.SubjectId.ToString());
        principal.FindFirstValue("auth_type")
            .Should().Be("machine");
        principal.FindFirstValue("subject_type")
            .Should().Be(nameof(SubjectType.ServiceAccount));

        principal.Claims
            .Where(c => c.Type == "scope")
            .Select(c => c.Value)
            .Should().BeEquivalentTo(["repo:read", "repo:write"]);
    }

    // -----------------------------------------------------------------------
    // Valid key → LastUsedAt is updated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidKey_UpdatesLastUsedAt()
    {
        var (key, rawSecret) = await SeedApiKeyAsync();
        key.LastUsedAt.Should().BeNull("no prior use");

        var (handler, _) = await BuildHandlerAsync($"ApiKey {key.Id}.{rawSecret}");
        var before = DateTimeOffset.UtcNow;

        await handler.AuthenticateAsync();

        var updated = await _db.ApiKeys.SingleAsync(k => k.Id == key.Id);
        updated.LastUsedAt.Should().NotBeNull();
        updated.LastUsedAt!.Value.Should().BeOnOrAfter(before);
    }

    // -----------------------------------------------------------------------
    // Scheme prefix is case-insensitive
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SchemePrefixIsCaseInsensitive_ReturnsSuccess()
    {
        var (key, rawSecret) = await SeedApiKeyAsync();

        var (handler, _) = await BuildHandlerAsync($"APIKEY {key.Id}.{rawSecret}");

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue("scheme prefix matching must be case-insensitive");
    }

    // -----------------------------------------------------------------------
    // Helper: minimal IOptionsMonitor implementation
    // -----------------------------------------------------------------------

    private sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
