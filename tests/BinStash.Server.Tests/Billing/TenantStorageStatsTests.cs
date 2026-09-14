// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.HostedServices;
using BinStash.Server.Services.Billing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinStash.Server.Tests.Billing;

public class TenantStorageStatsTests
{
    private static DbContextOptions<BinStashDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task RunOnce_PublishesADeduplicatedFootprintForEveryTenant()
    {
        var dbOptions = CreateOptions();
        var (tenant1Id, tenant2Id) = await SeedTwoTenantsAsync(dbOptions);

        var calculator = new StubCalculator
        {
            Footprints =
            {
                [tenant1Id] = 300,
                [tenant2Id] = 500
            }
        };
        var publisher = new SpyPublisher();

        await RunOnceAsync(dbOptions, calculator, publisher);

        publisher.Published.Should().HaveCount(2);
        publisher.Published.Should().Contain(s => s.TenantId == tenant1Id && s.UniqueLogicalBytes == 300);
        publisher.Published.Should().Contain(s => s.TenantId == tenant2Id && s.UniqueLogicalBytes == 500);
    }

    [Fact]
    public async Task RunOnce_KeepsThePreviousSnapshotForATenantThatCannotBeWalked()
    {
        // An unreadable release in one tenant must not publish a low figure for that tenant, and
        // must not stop every other tenant from being measured. Publishing a partial walk would
        // undercharge silently, which is the one failure nobody notices.
        var dbOptions = CreateOptions();
        var (brokenTenantId, healthyTenantId) = await SeedTwoTenantsAsync(dbOptions);

        var calculator = new StubCalculator
        {
            Footprints = { [healthyTenantId] = 500 },
            Unavailable = { brokenTenantId }
        };
        var publisher = new SpyPublisher();

        await RunOnceAsync(dbOptions, calculator, publisher);

        publisher.Published.Should().ContainSingle()
            .Which.TenantId.Should().Be(healthyTenantId);
    }

    private static async Task RunOnceAsync(DbContextOptions<BinStashDbContext> dbOptions, ITenantFootprintCalculator calculator, ITenantFootprintPublisher publisher)
    {
        var scopeFactory = new FakeServiceScopeFactory(dbOptions, calculator, publisher);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:StorageStatsIntervalMinutes"] = "60" })
            .Build();

        var sut = new TenantStorageStatsHostedService(scopeFactory, configuration, NullLogger<TenantStorageStatsHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        try { await sut.StopAsync(CancellationToken.None); } catch { /* ignore */ }
    }

    private static async Task<(Guid First, Guid Second)> SeedTwoTenantsAsync(DbContextOptions<BinStashDbContext> dbOptions)
    {
        await using var seedDb = new BinStashDbContext(dbOptions);

        var first = new Tenant { Name = "tenant-one", Slug = "tenant-one" };
        var second = new Tenant { Name = "tenant-two", Slug = "tenant-two" };
        seedDb.Tenants.AddRange(first, second);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (first.Id, second.Id);
    }

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class StubCalculator : ITenantFootprintCalculator
    {
        public Dictionary<Guid, long> Footprints { get; } = [];
        public HashSet<Guid> Unavailable { get; } = [];

        public Task<TenantStorageSnapshot> ComputeAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (Unavailable.Contains(tenantId))
                throw new TenantFootprintUnavailableException($"cannot walk tenant {tenantId}");

            return Task.FromResult(new TenantStorageSnapshot
            {
                TenantId = tenantId,
                ComputedAt = DateTimeOffset.UtcNow,
                UniqueLogicalBytes = Footprints.GetValueOrDefault(tenantId)
            });
        }
    }

    private sealed class SpyPublisher : ITenantFootprintPublisher
    {
        public List<TenantStorageSnapshot> Published { get; } = [];

        public Task PublishAsync(TenantStorageSnapshot snapshot, CancellationToken ct = default)
        {
            Published.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceScopeFactory : IServiceScopeFactory
    {
        private readonly DbContextOptions<BinStashDbContext> _dbOptions;
        private readonly ITenantFootprintCalculator _calculator;
        private readonly ITenantFootprintPublisher _publisher;

        public FakeServiceScopeFactory(DbContextOptions<BinStashDbContext> dbOptions, ITenantFootprintCalculator calculator, ITenantFootprintPublisher publisher)
        {
            _dbOptions = dbOptions;
            _calculator = calculator;
            _publisher = publisher;
        }

        public IServiceScope CreateScope() => new FakeServiceScope(_dbOptions, _calculator, _publisher);
    }

    private sealed class FakeServiceScope : IServiceScope
    {
        private readonly BinStashDbContext _scopedDb;
        public IServiceProvider ServiceProvider { get; }

        public FakeServiceScope(DbContextOptions<BinStashDbContext> dbOptions, ITenantFootprintCalculator calculator, ITenantFootprintPublisher publisher)
        {
            _scopedDb = new BinStashDbContext(dbOptions);
            var services = new ServiceCollection();
            services.AddSingleton(_scopedDb);
            services.AddSingleton(calculator);
            services.AddSingleton(publisher);
            ServiceProvider = services.BuildServiceProvider();
        }

        public void Dispose() => _scopedDb.Dispose();
    }
}
