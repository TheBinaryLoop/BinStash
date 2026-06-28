// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.HostedServices;
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
    public async Task RunOnce_CallsRecordStorageSnapshotAsync_ForEachTenantWithCorrectBytes()
    {
        // Arrange — seed data using a dedicated DbContext
        var dbOptions = CreateOptions();

        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();

        await using (var seedDb = new BinStashDbContext(dbOptions))
        {
            var store1 = new ChunkStore("store1", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/t1" });
            var store2 = new ChunkStore("store2", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/t2" });
            seedDb.ChunkStores.AddRange(store1, store2);
            await seedDb.SaveChangesAsync();

            var repo1 = new Repository { Name = "repo1", ChunkStoreId = store1.Id, ChunkStore = store1, TenantId = tenant1Id };
            var repo2 = new Repository { Name = "repo2", ChunkStoreId = store2.Id, ChunkStore = store2, TenantId = tenant2Id };
            seedDb.Repositories.AddRange(repo1, repo2);
            await seedDb.SaveChangesAsync();

            // Each release needs its own IngestSession (one-to-one with ReleaseMetrics)
            var session1a = MakeSession(repo1);
            var session1b = MakeSession(repo1);
            var session2 = MakeSession(repo2);
            seedDb.IngestSessions.AddRange(session1a, session1b, session2);
            await seedDb.SaveChangesAsync();

            var release1a = new Release { Id = Guid.NewGuid(), Version = "1.0", RepoId = repo1.Id, SerializerVersion = 1 };
            var release1b = new Release { Id = Guid.NewGuid(), Version = "1.1", RepoId = repo1.Id, SerializerVersion = 1 };
            var release2 = new Release { Id = Guid.NewGuid(), Version = "2.0", RepoId = repo2.Id, SerializerVersion = 1 };
            seedDb.Releases.AddRange(release1a, release1b, release2);
            await seedDb.SaveChangesAsync();

            // Tenant1: 100 + 200 = 300 bytes; Tenant2: 500 bytes
            seedDb.ReleaseMetrics.AddRange(
                new ReleaseMetrics { ReleaseId = release1a.Id, IngestSessionId = session1a.Id, IngestSession = session1a, TotalLogicalBytes = 100, CreatedAt = DateTimeOffset.UtcNow },
                new ReleaseMetrics { ReleaseId = release1b.Id, IngestSessionId = session1b.Id, IngestSession = session1b, TotalLogicalBytes = 200, CreatedAt = DateTimeOffset.UtcNow },
                new ReleaseMetrics { ReleaseId = release2.Id, IngestSessionId = session2.Id, IngestSession = session2, TotalLogicalBytes = 500, CreatedAt = DateTimeOffset.UtcNow }
            );
            await seedDb.SaveChangesAsync();
        }

        // Act — run the hosted service using a fresh scope per call
        var meteringService = new SpyUsageMeteringService();
        var scopeFactory = new FakeServiceScopeFactory(dbOptions, meteringService);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:StorageStatsIntervalMinutes"] = "60" })
            .Build();

        var sut = new TenantStorageStatsHostedService(scopeFactory, configuration, NullLogger<TenantStorageStatsHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        try { await sut.StopAsync(CancellationToken.None); } catch { /* ignore */ }

        // Assert
        meteringService.Calls.Should().HaveCount(2);
        meteringService.Calls.Should().Contain(c => c.TenantId == tenant1Id && c.Bytes == 300);
        meteringService.Calls.Should().Contain(c => c.TenantId == tenant2Id && c.Bytes == 500);
    }

    private static IngestSession MakeSession(Repository repo) => new()
    {
        Id = Guid.NewGuid(),
        RepoId = repo.Id,
        Repository = repo,
        State = IngestSessionState.Created,
        StartedAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
    };

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class SpyUsageMeteringService : IUsageMeteringService
    {
        public List<(Guid TenantId, long Bytes)> Calls { get; } = [];

        public void RecordIngest(Guid tenantId, long bytes) { }
        public void RecordEgress(Guid tenantId, long bytes) { }

        public Task RecordStorageSnapshotAsync(Guid tenantId, long bytes, CancellationToken ct = default)
        {
            Calls.Add((tenantId, bytes));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceScopeFactory : IServiceScopeFactory
    {
        private readonly DbContextOptions<BinStashDbContext> _dbOptions;
        private readonly IUsageMeteringService _meteringService;

        public FakeServiceScopeFactory(DbContextOptions<BinStashDbContext> dbOptions, IUsageMeteringService meteringService)
        {
            _dbOptions = dbOptions;
            _meteringService = meteringService;
        }

        public IServiceScope CreateScope() => new FakeServiceScope(_dbOptions, _meteringService);
    }

    private sealed class FakeServiceScope : IServiceScope
    {
        private readonly BinStashDbContext _scopedDb;
        public IServiceProvider ServiceProvider { get; }

        public FakeServiceScope(DbContextOptions<BinStashDbContext> dbOptions, IUsageMeteringService meteringService)
        {
            _scopedDb = new BinStashDbContext(dbOptions);
            var services = new ServiceCollection();
            services.AddSingleton(_scopedDb);
            services.AddSingleton(meteringService);
            ServiceProvider = services.BuildServiceProvider();
        }

        public void Dispose() => _scopedDb.Dispose();
    }
}
