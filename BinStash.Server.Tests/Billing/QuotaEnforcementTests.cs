// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Billing;
using BinStash.Server.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace BinStash.Server.Tests.Billing;

public class QuotaEnforcementTests : IDisposable
{
    private readonly BinStashDbContext _db;

    public QuotaEnforcementTests()
    {
        var options = new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BinStashDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // -------------------------------------------------------------------------
    // BillingLimitsCache unit tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCachedLimitsAsync_WhenIngestNotAllowed_ReturnsLimitsWithIsIngestAllowedFalse()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var provider = new StubBillingProvider(isIngestAllowed: false);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new BillingLimitsCache(provider, cache);

        // Act
        var limits = await sut.GetCachedLimitsAsync(tenantId);

        // Assert
        limits.IsIngestAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetCachedLimitsAsync_CachesResult_ProviderCalledOnlyOnce()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var provider = new StubBillingProvider(isIngestAllowed: true);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new BillingLimitsCache(provider, cache);

        // Act
        await sut.GetCachedLimitsAsync(tenantId);
        await sut.GetCachedLimitsAsync(tenantId);
        await sut.GetCachedLimitsAsync(tenantId);

        // Assert
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetCachedLimitsAsync_DifferentTenants_CachedSeparately()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var provider = new StubBillingProvider(isIngestAllowed: true);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new BillingLimitsCache(provider, cache);

        // Act
        await sut.GetCachedLimitsAsync(tenantA);
        await sut.GetCachedLimitsAsync(tenantB);

        // Assert — two distinct tenants → two provider calls
        provider.CallCount.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Endpoint integration: 402 when IsIngestAllowed = false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateIngestSession_WhenIngestNotAllowed_Returns402()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var repoId = Guid.NewGuid();

        var store = new ChunkStore("test-store", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/test" });
        var repo = new Repository { Name = "test-repo", ChunkStore = store, TenantId = tenantId };
        typeof(Repository).GetProperty("Id")!.SetValue(repo, repoId);

        _db.ChunkStores.Add(store);
        _db.Repositories.Add(repo);
        await _db.SaveChangesAsync();

        var provider = new StubBillingProvider(isIngestAllowed: false);
        var memCache = new MemoryCache(new MemoryCacheOptions());
        var billingCache = new BillingLimitsCache(provider, memCache);

        // Act — call the handler directly via reflection (it's private static)
        var method = typeof(IngestSessionEndpoints)
            .GetMethod("CreateIngestSessionAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = await (Task<IResult>)method.Invoke(null, [tenantId, repoId, null, _db, billingCache, CancellationToken.None])!;

        // Assert
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services.BuildServiceProvider();
        httpContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(402);
    }

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class StubBillingProvider(bool isIngestAllowed) : IBillingProvider
    {
        public int CallCount { get; private set; }

        public Task<IBillingLimits> GetLimitsAsync(Guid tenantId, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<IBillingLimits>(new StubLimits(isIngestAllowed));
        }
    }

    private sealed class StubLimits(bool isIngestAllowed) : IBillingLimits
    {
        public bool IsStorageAllowed => true;
        public bool IsIngestAllowed { get; } = isIngestAllowed;
        public bool IsEgressAllowed => true;
        public long MaxStorageBytes => long.MaxValue;
    }
}
