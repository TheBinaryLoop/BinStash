// Copyright (C) Lukas Eßmann — AGPLv3 or later

using System.Reflection;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Billing;
using BinStash.Core.Entities;
using BinStash.Core.Storage.Stats;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage.FileDefinition;
using BinStash.Server.Endpoints;
using BinStash.Server.Services.ChunkStores;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinStash.Server.Tests.Billing;

public class EgressMeterTests : IDisposable
{
    private readonly BinStashDbContext _db;

    public EgressMeterTests()
    {
        var options = new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BinStashDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetReleaseDownload_CallsRecordEgress_WithActualBytesWritten()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var repoId = Guid.NewGuid();

        var store = new ChunkStore("test-store", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/test" });
        var repo = new Repository { Name = "test-repo", ChunkStore = store, TenantId = tenantId };
        typeof(Repository).GetProperty("Id")!.SetValue(repo, repoId);

        var release = new Release
        {
            Id = releaseId,
            Version = "1.0.0",
            RepoId = repoId,
            Repository = repo,
            ReleaseDefinitionChecksum = default,
            CreatedAt = DateTimeOffset.UtcNow,
            SerializerVersion = 0,
        };

        _db.ChunkStores.Add(store);
        _db.Repositories.Add(repo);
        _db.Releases.Add(release);
        await _db.SaveChangesAsync();

        var meteringService = new SpyEgressMeteringService();
        var loggerFactory = NullLoggerFactory.Instance;

        // The handler is private static — invoke via reflection
        var method = typeof(ReleaseEndpoints).GetMethod(
            "GetReleaseDownloadAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("GetReleaseDownloadAsync must exist as a private static method");

        var httpContext = new DefaultHttpContext();
        var response = httpContext.Response;
        response.Body = new MemoryStream();

        // Act — StubChunkStoreService returns a package with one zero-length opaque artifact
        // so the handler reaches the streaming section and the egress metering call.
        var stubChunkStore = new StubChunkStoreService();
        var task = (Task<IResult>)method!.Invoke(null, [
            tenantId,
            releaseId,
            null,           // component
            null,           // file
            (Guid?)null,    // diffReleaseId
            response,
            _db,
            stubChunkStore,
            meteringService,
            loggerFactory
        ])!;

        await task;

        // Assert — RecordEgress called with correct tenantId and actual bytes written (> 0 from tar header)
        meteringService.Calls.Should().HaveCount(1);
        meteringService.Calls[0].TenantId.Should().Be(tenantId);
        meteringService.Calls[0].Bytes.Should().BeGreaterThan(0,
            "the tar.zst stream must contain at least the tar header bytes");
    }

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class SpyEgressMeteringService : IUsageMeteringService
    {
        public List<(Guid TenantId, long Bytes)> Calls { get; } = [];

        public void RecordIngest(Guid tenantId, long bytes) { }
        public void RecordEgress(Guid tenantId, long bytes) => Calls.Add((tenantId, bytes));
        public Task RecordStorageSnapshotAsync(Guid tenantId, long bytes, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Returns a release package with one zero-length opaque artifact (no chunks) so the handler
    /// reaches the streaming section and the egress metering call.
    /// The file definition returns empty chunk data (varint 0 = no chunks).
    /// The artifact has Length=0 so no chunk retrieval is needed.
    /// </summary>
    private sealed class StubChunkStoreService : IChunkStoreService
    {
        // Fake content hash — all zeros
        private static readonly Hash32 FakeHash = default;

        public Task<(bool Success, int BytesWritten)> StoreChunkAsync(ChunkStore store, string chunkId, ReadOnlyMemory<byte> chunkData)
            => Task.FromResult((true, chunkData.Length));

        public Task<byte[]?> RetrieveChunkAsync(ChunkStore store, string chunkId)
            => Task.FromResult<byte[]?>(null);

        public Task<(bool Success, Hash32 FileHash, int BytesWritten)> StoreFileDefinitionAsync(ChunkStore store, ReadOnlyMemory<byte> data)
            => Task.FromResult((true, FakeHash, data.Length));

        public Task<byte[]?> RetrieveFileDefinitionAsync(ChunkStore store, string fileHash)
        {
            // Return a valid FileDefinitionRecord with no chunks.
            var record = new FileDefinitionRecord
            {
                FileHash = FakeHash,
                FileLength = 0,
                ChunkHashes = [],
            };
            return Task.FromResult<byte[]?>(record.Serialize());
        }

        public Task<bool> StoreReleasePackageAsync(ChunkStore store, ReadOnlyMemory<byte> packageData)
            => Task.FromResult(true);

        public Task<byte[]?> RetrieveReleasePackageAsync(ChunkStore store, string packageId)
        {
            // Package with one zero-length opaque artifact so the handler reaches streaming
            var package = new ReleasePackage
            {
                OutputArtifacts =
                [
                    new OutputArtifact
                    {
                        Path = "test-component/test-file.bin",
                        ComponentName = "test-component",
                        Kind = OutputArtifactKind.File,
                        RequiresBytePerfectReconstruction = false,
                        Backing = new OpaqueBlobBacking
                        {
                            ContentHash = FakeHash,
                            Length = 0,  // zero-length file — no chunk retrieval needed
                        }
                    }
                ],
            };
            var bytes = BinStash.Core.Serialization.ReleasePackageSerializer.SerializeAsync(package).GetAwaiter().GetResult().Data;
            return Task.FromResult<byte[]?>(bytes);
        }

        public Task<bool> DeleteReleasePackageAsync(ChunkStore store, string packageId)
            => Task.FromResult(true);

        public Task<bool> RebuildStorageAsync(ChunkStore store)
            => Task.FromResult(true);

        public Task<bool> RebuildStorageWithProgressAsync(ChunkStore store, IProgress<bool> progress, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<Dictionary<string, byte[]>> RetrieveFileDefinitionsAsync(ChunkStore store, IReadOnlyCollection<string> fileHashes)
            => Task.FromResult(new Dictionary<string, byte[]>());

        public Task<Dictionary<string, byte[]>> RetrieveReleasePackagesAsync(ChunkStore store, IReadOnlyCollection<string> packageIds)
            => Task.FromResult(new Dictionary<string, byte[]>());

        public Task<ChunkStorePhysicalStats> GetPhysicalStatsAsync(ChunkStore store)
            => Task.FromResult(new ChunkStorePhysicalStats());
    }
}
