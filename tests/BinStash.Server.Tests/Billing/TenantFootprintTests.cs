// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Services.Billing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinStash.Server.Tests.Billing;

public class TenantFootprintRefreshQueueTests
{
    [Fact]
    public async Task Enqueue_CollapsesRepeatedRequestsForTheSameTenant()
    {
        // A build matrix finalizes one ingest per target within seconds of each other. Each one
        // asks for a refresh; one walk must answer all of them.
        var queue = new TenantFootprintRefreshQueue();
        var tenantId = Guid.NewGuid();

        queue.Enqueue(tenantId);
        queue.Enqueue(tenantId);
        queue.Enqueue(tenantId);

        var drained = await DrainAsync(queue, expected: 1);

        drained.Should().ContainSingle().Which.Should().Be(tenantId);
    }

    [Fact]
    public async Task Enqueue_AfterRelease_QueuesTheTenantAgain()
    {
        // An ingest landing while a walk is already running must not be swallowed: its bytes
        // would then wait for the daily sweep.
        var queue = new TenantFootprintRefreshQueue();
        var tenantId = Guid.NewGuid();

        queue.Enqueue(tenantId);
        queue.Release(tenantId);
        queue.Enqueue(tenantId);

        var drained = await DrainAsync(queue, expected: 2);

        drained.Should().HaveCount(2).And.OnlyContain(x => x == tenantId);
    }

    [Fact]
    public async Task Enqueue_KeepsDistinctTenantsSeparate()
    {
        var queue = new TenantFootprintRefreshQueue();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Enqueue(first);

        var drained = await DrainAsync(queue, expected: 2);

        drained.Should().BeEquivalentTo([first, second]);
    }

    private static async Task<List<Guid>> DrainAsync(TenantFootprintRefreshQueue queue, int expected)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var drained = new List<Guid>();

        await foreach (var id in queue.ReadAllAsync(cts.Token))
        {
            drained.Add(id);
            if (drained.Count == expected)
                break;
        }

        return drained;
    }
}

public class TenantFootprintPublisherTests : IDisposable
{
    private readonly BinStashDbContext _db;

    public TenantFootprintPublisherTests()
    {
        var options = new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BinStashDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task PublishAsync_MetersTheDeduplicatedFigure_NotTheLogicalOne()
    {
        // The whole point of the model: a tenant is charged for what they hold, not for the sum
        // of their releases. A five-target release whose targets share most of their content must
        // not meter five times.
        var metering = new RecordingMeteringService();
        var publisher = new TenantFootprintPublisher(_db, metering, NullLogger<TenantFootprintPublisher>.Instance);

        var tenantId = Guid.NewGuid();
        var snapshot = new TenantStorageSnapshot
        {
            TenantId = tenantId,
            ComputedAt = DateTimeOffset.UtcNow,
            UniqueLogicalBytes = 610_000_000,
            LogicalBytes = 2_400_000_000
        };

        await publisher.PublishAsync(snapshot, TestContext.Current.CancellationToken);

        metering.StorageSnapshots.Should().ContainSingle()
            .Which.Should().Be((tenantId, 610_000_000L));
    }

    [Fact]
    public async Task PublishAsync_PersistsTheSnapshotForLaterReads()
    {
        // The usage page and the quota gate both read the newest snapshot rather than re-walking,
        // so the row has to survive the call that produced it.
        var publisher = new TenantFootprintPublisher(_db, new RecordingMeteringService(), NullLogger<TenantFootprintPublisher>.Instance);

        var tenantId = Guid.NewGuid();
        await publisher.PublishAsync(new TenantStorageSnapshot
        {
            TenantId = tenantId,
            ComputedAt = DateTimeOffset.UtcNow,
            UniqueLogicalBytes = 42,
            LogicalBytes = 100,
            UniqueChunkCount = 7
        }, TestContext.Current.CancellationToken);

        var stored = await _db.TenantStorageSnapshots.SingleAsync(x => x.TenantId == tenantId, TestContext.Current.CancellationToken);

        stored.UniqueLogicalBytes.Should().Be(42);
        stored.LogicalBytes.Should().Be(100);
        stored.UniqueChunkCount.Should().Be(7);
    }

    private sealed class RecordingMeteringService : IUsageMeteringService
    {
        public List<(Guid TenantId, long Bytes)> StorageSnapshots { get; } = [];

        public void RecordIngest(Guid tenantId, long bytes) { }

        public void RecordEgress(Guid tenantId, long bytes) { }

        public Task RecordStorageSnapshotAsync(Guid tenantId, long bytes, CancellationToken ct = default)
        {
            StorageSnapshots.Add((tenantId, bytes));
            return Task.CompletedTask;
        }
    }
}
