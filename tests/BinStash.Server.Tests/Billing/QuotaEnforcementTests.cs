// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Server.Services.Usage;
using BinStash.Core.Auditing;
using BinStash.Contracts.Release;
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
        var limits = await sut.GetCachedLimitsAsync(tenantId, TestContext.Current.CancellationToken);

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
        await sut.GetCachedLimitsAsync(tenantId, TestContext.Current.CancellationToken);
        await sut.GetCachedLimitsAsync(tenantId, TestContext.Current.CancellationToken);
        await sut.GetCachedLimitsAsync(tenantId, TestContext.Current.CancellationToken);

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
        await sut.GetCachedLimitsAsync(tenantA, TestContext.Current.CancellationToken);
        await sut.GetCachedLimitsAsync(tenantB, TestContext.Current.CancellationToken);

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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var quota = BuildQuotaGuard(new StubBillingProvider(isIngestAllowed: false));

        // Act — call the handler directly via reflection (it's private static)
        var method = typeof(IngestSessionEndpoints)
            .GetMethod("CreateIngestSessionAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = await (Task<IResult>)method.Invoke(null, [tenantId, repoId, null, _db, quota, CancellationToken.None])!;

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
    // Storage quota — the ceiling that previously had no enforcement site
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateIngestSession_WhenStorageQuotaAlreadyReached_Returns402()
    {
        var (tenantId, repoId) = await SeedTenantWithUsageAsync(logicalBytes: 1_000);

        var quota = BuildQuotaGuard(new StubBillingProvider(maxStorageBytes: 1_000));

        var method = typeof(IngestSessionEndpoints)
            .GetMethod("CreateIngestSessionAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = await (Task<IResult>)method.Invoke(null, [tenantId, repoId, null, _db, quota, CancellationToken.None])!;

        (await StatusCodeOfAsync(result)).Should().Be(402,
            "usage has reached the ceiling, so a new session must not be admitted");
    }

    [Fact]
    public async Task CreateIngestSession_WhenStorageNotAllowed_Returns402()
    {
        var (tenantId, repoId) = await SeedTenantWithUsageAsync(logicalBytes: 0);

        var quota = BuildQuotaGuard(new StubBillingProvider(isStorageAllowed: false));

        var method = typeof(IngestSessionEndpoints)
            .GetMethod("CreateIngestSessionAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = await (Task<IResult>)method.Invoke(null, [tenantId, repoId, null, _db, quota, CancellationToken.None])!;

        (await StatusCodeOfAsync(result)).Should().Be(402);
    }

    [Fact]
    public async Task CheckStorageCommit_WhenReleaseWouldCrossTheCeiling_Denies()
    {
        var (tenantId, _) = await SeedTenantWithUsageAsync(logicalBytes: 900);
        var quota = BuildQuotaGuard(new StubBillingProvider(maxStorageBytes: 1_000));

        var decision = await quota.CheckStorageCommitAsync(tenantId, 200, TestContext.Current.CancellationToken);

        decision.IsAllowed.Should().BeFalse(
            "900 already stored plus 200 more exceeds the 1000 ceiling — this is the check that makes the quota a quota");
        decision.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckStorageCommit_WhenReleaseFitsExactly_Allows()
    {
        var (tenantId, _) = await SeedTenantWithUsageAsync(logicalBytes: 900);
        var quota = BuildQuotaGuard(new StubBillingProvider(maxStorageBytes: 1_000));

        var decision = await quota.CheckStorageCommitAsync(tenantId, 100, TestContext.Current.CancellationToken);

        decision.IsAllowed.Should().BeTrue("landing exactly on the ceiling is within it");
    }

    [Fact]
    public async Task CheckStorageCommit_WhenUnlimited_Allows()
    {
        var (tenantId, _) = await SeedTenantWithUsageAsync(logicalBytes: 10_000);
        var quota = BuildQuotaGuard(new StubBillingProvider());

        var decision = await quota.CheckStorageCommitAsync(tenantId, long.MaxValue / 2, TestContext.Current.CancellationToken);

        decision.IsAllowed.Should().BeTrue(
            "long.MaxValue from the NoOp provider means 'no plan limit', not a ceiling that happens to be high");
    }

    // -------------------------------------------------------------------------
    // Egress quota
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CheckEgress_WhenEgressNotAllowed_Denies()
    {
        var quota = BuildQuotaGuard(new StubBillingProvider(isEgressAllowed: false));

        var decision = await quota.CheckEgressAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        decision.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task CheckEgress_WhenAllowed_Allows()
    {
        var quota = BuildQuotaGuard(new StubBillingProvider());

        var decision = await quota.CheckEgressAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        decision.IsAllowed.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Refusals are recorded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QuotaRefusal_IsAudited()
    {
        var audit = new RecordingAuditLogWriter();
        var quota = new TenantQuotaGuard(
            new BillingLimitsCache(new StubBillingProvider(isEgressAllowed: false), new MemoryCache(new MemoryCacheOptions())),
            new TenantUsageService(_db),
            audit);

        var tenantId = Guid.NewGuid();
        await quota.CheckEgressAsync(tenantId, TestContext.Current.CancellationToken);

        var entry = audit.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditActions.QuotaExceeded);
        entry.Outcome.Should().Be(AuditOutcome.Denied);
        entry.TenantId.Should().Be(tenantId);
        entry.Metadata!["resource"].Should().Be("egress");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Seeds a tenant holding <paramref name="logicalBytes"/>, through the full
    /// repository → release → metrics chain the usage query actually joins over.
    /// </summary>
    private async Task<(Guid TenantId, Guid RepoId)> SeedTenantWithUsageAsync(long logicalBytes)
    {
        var tenantId = Guid.NewGuid();
        var repoId = Guid.NewGuid();

        var store = new ChunkStore("test-store", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/test" });
        _db.ChunkStores.Add(store);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new Repository { Name = "test-repo", ChunkStoreId = store.Id, ChunkStore = store, TenantId = tenantId };
        typeof(Repository).GetProperty("Id")!.SetValue(repo, repoId);
        _db.Repositories.Add(repo);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (logicalBytes > 0)
        {
            var session = new IngestSession
            {
                Id = Guid.NewGuid(),
                RepoId = repoId,
                Repository = repo,
                State = IngestSessionState.Completed,
                StartedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            };
            _db.IngestSessions.Add(session);
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var release = new Release { Id = Guid.NewGuid(), Version = "1.0.0", RepoId = repoId };
            _db.Releases.Add(release);

            var variant = new ReleaseVariant
            {
                ReleaseId = release.Id,
                Release = release,
                TargetKey = ReleaseTarget.Default,
                SerializerVersion = 1,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ReleaseVariants.Add(variant);
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

            _db.ReleaseMetrics.Add(new ReleaseMetrics
            {
                VariantId = variant.Id,
                IngestSessionId = session.Id,
                IngestSession = session,
                TotalLogicalBytes = (ulong)logicalBytes,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return (tenantId, repoId);
    }

    private static async Task<int> StatusCodeOfAsync(IResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        httpContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(httpContext);
        return httpContext.Response.StatusCode;
    }

    private sealed class RecordingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditEntryDraft> Entries { get; } = [];

        public Task WriteAsync(AuditEntryDraft entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private TenantQuotaGuard BuildQuotaGuard(StubBillingProvider provider)
        => new(new BillingLimitsCache(provider, new MemoryCache(new MemoryCacheOptions())), new TenantUsageService(_db), new NoOpAuditLogWriter());

    private sealed class StubBillingProvider(bool isIngestAllowed = true, bool isStorageAllowed = true, bool isEgressAllowed = true, long maxStorageBytes = long.MaxValue) : IBillingProvider
    {
        public int CallCount { get; private set; }

        public Task<IBillingLimits> GetLimitsAsync(Guid tenantId, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<IBillingLimits>(new StubLimits(isIngestAllowed, isStorageAllowed, isEgressAllowed, maxStorageBytes));
        }
    }

    private sealed class StubLimits(bool isIngestAllowed, bool isStorageAllowed, bool isEgressAllowed, long maxStorageBytes) : IBillingLimits
    {
        public bool IsStorageAllowed { get; } = isStorageAllowed;
        public bool IsIngestAllowed { get; } = isIngestAllowed;
        public bool IsEgressAllowed { get; } = isEgressAllowed;
        public long MaxStorageBytes { get; } = maxStorageBytes;
    }

    private sealed class NoOpAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditEntryDraft entry, CancellationToken ct = default) => Task.CompletedTask;
    }
}
