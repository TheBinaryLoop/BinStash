// Copyright (C) Lukas Eßmann — AGPLv3 or later

using System.Security.Claims;
using BinStash.Contracts.Hashing;
using BinStash.Core.Billing;
using BinStash.Core.Entities;
using BinStash.Core.Storage.Stats;
using BinStash.Grpc;
using BinStash.Infrastructure.Data;
using BinStash.Server.Grpc;
using BinStash.Server.Services.ChunkStores;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinStash.Server.Tests.Billing;

public class IngestMeterTests : IDisposable
{
    private readonly BinStashDbContext _db;

    public IngestMeterTests()
    {
        var options = new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BinStashDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task UploadChunks_CallsRecordIngest_WithCorrectTenantIdAndByteCount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var repoId = Guid.NewGuid();
        var ingestId = Guid.NewGuid();

        var store = new ChunkStore("test-store", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/test" });
        var repo = new Repository { Name = "test-repo", ChunkStore = store, TenantId = tenantId };
        // Override the auto-generated Id so we can reference it in headers
        typeof(Repository).GetProperty("Id")!.SetValue(repo, repoId);

        var session = new IngestSession
        {
            Id = ingestId,
            RepoId = repoId,
            Repository = repo,
            State = IngestSessionState.Created,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        _db.ChunkStores.Add(store);
        _db.Repositories.Add(repo);
        _db.IngestSessions.Add(session);
        await _db.SaveChangesAsync();

        var chunkData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var chunkHash = Blake3.Hasher.Hash(chunkData);
        var hashBytes = ByteString.CopyFrom(chunkHash.AsSpan());
        var dataBytes = ByteString.CopyFrom(chunkData);

        var request = new UploadChunkRequest
        {
            Checksum = hashBytes,
            Data = dataBytes
        };

        var meteringService = new SpyUsageMeteringService();
        var chunkStoreService = new AlwaysSucceedChunkStoreService();
        var authorizationService = new AlwaysSucceedAuthorizationService();

        var svc = new IngestGrpcService(
            _db,
            chunkStoreService,
            authorizationService,
            meteringService,
            NullLogger<IngestGrpcService>.Instance);

        var stream = new SingleItemAsyncStreamReader<UploadChunkRequest>(request);
        var callContext = new FakeServerCallContext(ingestId, repoId);

        // Act
        await svc.UploadChunks(stream, callContext);

        // Assert
        meteringService.Calls.Should().HaveCount(1);
        meteringService.Calls[0].TenantId.Should().Be(tenantId);
        meteringService.Calls[0].Bytes.Should().Be(chunkData.Length);
    }

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    private sealed class SpyUsageMeteringService : IUsageMeteringService
    {
        public List<(Guid TenantId, long Bytes)> Calls { get; } = [];

        public void RecordIngest(Guid tenantId, long bytes) => Calls.Add((tenantId, bytes));
        public void RecordEgress(Guid tenantId, long bytes) { }
        public Task RecordStorageSnapshotAsync(Guid tenantId, long bytes, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AlwaysSucceedChunkStoreService : IChunkStoreService
    {
        public Task<(bool Success, int BytesWritten)> StoreChunkAsync(ChunkStore store, string chunkId, ReadOnlyMemory<byte> chunkData)
            => Task.FromResult((true, chunkData.Length));

        public Task<byte[]?> RetrieveChunkAsync(ChunkStore store, string chunkId)
            => Task.FromResult<byte[]?>(null);

        public Task<(bool Success, Hash32 FileHash, int BytesWritten)> StoreFileDefinitionAsync(ChunkStore store, ReadOnlyMemory<byte> data)
            => Task.FromResult((true, default(Hash32), data.Length));

        public Task<byte[]?> RetrieveFileDefinitionAsync(ChunkStore store, string fileHash)
            => Task.FromResult<byte[]?>(null);

        public Task<bool> StoreReleasePackageAsync(ChunkStore store, ReadOnlyMemory<byte> packageData)
            => Task.FromResult(true);

        public Task<byte[]?> RetrieveReleasePackageAsync(ChunkStore store, string packageId)
            => Task.FromResult<byte[]?>(null);

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

    private sealed class AlwaysSucceedAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class SingleItemAsyncStreamReader<T>(T item) : IAsyncStreamReader<T>
    {
        private int _index;
        public T Current { get; private set; } = default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_index++ == 0)
            {
                Current = item;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    private sealed class FakeServerCallContext : ServerCallContext
    {
        private readonly Metadata _requestHeaders;

        public FakeServerCallContext(Guid ingestId, Guid repoId)
        {
            _requestHeaders = new Metadata
            {
                { "x-ingest-session-id", ingestId.ToString() },
                { "x-repo-id", repoId.ToString() }
            };

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = BuildServiceProvider();
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test");
            httpContext.User = new ClaimsPrincipal(identity);
            UserState["__HttpContext"] = httpContext;
        }

        private static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAuthentication();
            services.AddAuthorization();
            return services.BuildServiceProvider();
        }

        protected override string MethodCore => "UploadChunks";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "127.0.0.1";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => _requestHeaders;
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("test", []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
            => Task.CompletedTask;
    }
}
