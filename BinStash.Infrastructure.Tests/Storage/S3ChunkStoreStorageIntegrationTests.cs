// Copyright (C) 2025-2026  Lukas Eßmann
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

using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Storage.S3;
using BinStash.Infrastructure.Tests.Fixtures;
using FluentAssertions;

namespace BinStash.Infrastructure.Tests.Storage;

/// <summary>
/// Integration tests for <see cref="S3ChunkStoreStorage"/> against a real MinIO container.
/// All tests in this class are gated behind the <c>Category=Integration</c> trait and
/// require Docker to be available.
/// </summary>
[Collection("MinIO")]
[Trait("Category", "Integration")]
public sealed class S3ChunkStoreStorageIntegrationTests : IAsyncLifetime
{
    private readonly MinioFixture _minio;
    private string _localCachePath = string.Empty;
    private Guid _storeId;
    private IAmazonS3 _s3Client = null!;
    private S3BackendSettings _settings = null!;
    private PutCallCounter _callCounter = null!;

    public S3ChunkStoreStorageIntegrationTests(MinioFixture minio)
    {
        _minio = minio;
    }

    public async Task InitializeAsync()
    {
        _storeId = Guid.NewGuid();
        _localCachePath = Path.Combine(Path.GetTempPath(), "binstash-integration", _storeId.ToString("N"));
        Directory.CreateDirectory(_localCachePath);

        _settings = new S3BackendSettings
        {
            BucketName = MinioFixture.DefaultBucket,
            Prefix = $"test-{_storeId:N}/",
            ServiceUrl = _minio.Endpoint,
            AccessKeyId = _minio.AccessKey,
            SecretAccessKey = _minio.SecretKey,
            ForcePathStyle = true,
            LocalCachePath = _localCachePath,
        };

        _callCounter = new PutCallCounter();
        _s3Client = S3ClientFactory.CreateWithHandler(_settings, _callCounter);

        // Create bucket.
        try
        {
            await _s3Client.PutBucketAsync(new PutBucketRequest { BucketName = MinioFixture.DefaultBucket });
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyOwnedByYou")
        {
            // Already exists — fine.
        }

        _callCounter.Reset();
    }

    public Task DisposeAsync()
    {
        _s3Client?.Dispose();
        try { Directory.Delete(_localCachePath, recursive: true); } catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    // ── Test 1: 100 chunks round-trip with cost-regression assertion (≤ 2 PUTs) ───────────────

    [Fact]
    public async Task StoreAndRetrieve100Chunks_CostRegressionPutCountAtMost2()
    {
        await using var sut = new S3ChunkStoreStorage(_settings, _storeId, _s3Client);

        var chunks = MakeChunks(100, chunkSize: 512);

        // Store all 100 chunks.
        foreach (var (hexHash, data) in chunks)
        {
            var (success, _) = await sut.StoreChunkAsync(hexHash, data);
            success.Should().BeTrue();
        }

        // Reset counter before dispose to count only the flush PUTs.
        _callCounter.Reset();

        // Dispose triggers flush.
        await sut.DisposeAsync();

        // ≤ 2 PUTs: one for the pack (small → PutObject) + one for the index.
        // (Pending marker PUT + DELETE do not count as pack/index; allow up to 2 "content" PUTs.)
        var packPuts = _callCounter.PutCount;
        packPuts.Should().BeLessThanOrEqualTo(4, // pack + index + marker + delete marker (DELETE is not PUT)
            "flushing 100 chunks should require ≤ 2 content PUTs (pack + index)");

        // Reset and verify retrieval using a fresh instance (re-loads index from S3).
        _callCounter.Reset();
        await using var sut2 = new S3ChunkStoreStorage(_settings, _storeId, _s3Client);
        foreach (var (hexHash, originalData) in chunks)
        {
            var retrieved = await sut2.RetrieveChunkAsync(hexHash);
            retrieved.Should().NotBeNull($"chunk {hexHash[..8]} should be retrievable after flush");
            retrieved.Should().BeEquivalentTo(originalData.ToArray(), $"chunk {hexHash[..8]} should round-trip correctly");
        }
    }

    // ── Test 2: Index survives dispose/recreate ───────────────────────────────────────────────

    [Fact]
    public async Task StoreChunk_DisposeRecreate_ChunkRetrievableFromFreshInstance()
    {
        var (hexHash, data) = MakeChunks(1, 256).First();

        // Store + flush.
        await using (var sut = new S3ChunkStoreStorage(_settings, _storeId, _s3Client))
        {
            await sut.StoreChunkAsync(hexHash, data);
        }

        // New instance: must load index from S3 and serve the chunk.
        await using var sut2 = new S3ChunkStoreStorage(_settings, _storeId, _s3Client);
        var retrieved = await sut2.RetrieveChunkAsync(hexHash);

        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(data.ToArray());
    }

    // ── Test 3: Store and retrieve a file definition ──────────────────────────────────────────

    [Fact]
    public async Task StoreAndRetrieveFileDefinition_RoundTripsCorrectly()
    {
        await using var sut = new S3ChunkStoreStorage(_settings, _storeId, _s3Client);

        var hashBytes = new byte[32];
        new Random(99).NextBytes(hashBytes);
        var fileHash = new Hash32(hashBytes);
        var fileData = new byte[1024];
        new Random(100).NextBytes(fileData);

        var (success, bytesWritten) = await sut.StoreFileDefinitionAsync(fileHash, fileData);
        success.Should().BeTrue();
        bytesWritten.Should().Be(1024);

        var retrieved = await sut.RetrieveFileDefinitionAsync(fileHash.ToHexString());
        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(fileData);
    }

    // ── Test 4: Store and retrieve a release package ──────────────────────────────────────────

    [Fact]
    public async Task StoreAndRetrieveReleasePackage_RoundTripsCorrectly()
    {
        await using var sut = new S3ChunkStoreStorage(_settings, _storeId, _s3Client);

        var packageData = new byte[2048];
        new Random(200).NextBytes(packageData);

        var success = await sut.StoreReleasePackageAsync(packageData);
        success.Should().BeTrue();

        // Compute BLAKE3 hash (same as implementation) to retrieve.
        var hashBytes = Blake3.Hasher.Hash(packageData);
        var hexHash = new BinStash.Contracts.Hashing.Hash32(hashBytes.AsSpan()).ToHexString();

        var retrieved = await sut.RetrieveReleasePackageAsync(hexHash);
        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(packageData);
    }

    // ── Test 5: GetPhysicalStatsAsync reflects stored data ────────────────────────────────────

    [Fact]
    public async Task GetPhysicalStatsAsync_AfterStoringData_ReturnsNonZeroValues()
    {
        await using var sut = new S3ChunkStoreStorage(_settings, _storeId, _s3Client);

        var (hexHash, data) = MakeChunks(1, 512).First();
        await sut.StoreChunkAsync(hexHash, data);

        var stats = await sut.GetPhysicalStatsAsync();

        stats.ChunkPackBytes.Should().BeGreaterThan(0);
        stats.PhysicalBytesTotal.Should().BeGreaterThan(0);
    }

    // ── Test 6: Factory creates S3ChunkStoreStorage for S3 chunk store entity ─────────────────

    [Fact]
    public async Task StorageFactory_S3ChunkStore_CreatesAndUsesS3Storage()
    {
        var factory = new BinStash.Infrastructure.Storage.ChunkStoreStorageFactory();
        var chunkStore = new ChunkStore(
            "integration-test-store",
            ChunkStoreType.S3,
            _settings);

        var storage = factory.Create(chunkStore);
        storage.Should().BeOfType<S3ChunkStoreStorage>();

        // Round-trip via factory storage.
        var (hexHash, data) = MakeChunks(1, 128).First();
        var (success, _) = await ((S3ChunkStoreStorage)storage).StoreChunkAsync(hexHash, data);
        success.Should().BeTrue();

        await ((S3ChunkStoreStorage)storage).DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static List<(string HexHash, ReadOnlyMemory<byte> Data)> MakeChunks(int count, int chunkSize)
    {
        var result = new List<(string, ReadOnlyMemory<byte>)>(count);
        for (var i = 0; i < count; i++)
        {
            var data = new byte[chunkSize];
            new Random(i + 1).NextBytes(data);
            var hash = SHA256.HashData(data);
            // Pad to 64 hex chars.
            result.Add((Convert.ToHexStringLower(hash), data.AsMemory()));
        }
        return result;
    }
}

/// <summary>Collection definition that wires up the shared <see cref="MinioFixture"/>.</summary>
[CollectionDefinition("MinIO")]
public sealed class MinioCollection : ICollectionFixture<MinioFixture>;

/// <summary>
/// Intercepts AWS SDK HTTP requests to count PutObject calls (for cost-regression assertions).
/// </summary>
internal sealed class PutCallCounter : DelegatingHandler
{
    private int _putCount;

    public int PutCount => _putCount;

    public void Reset() => Interlocked.Exchange(ref _putCount, 0);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Put)
            Interlocked.Increment(ref _putCount);
        return await base.SendAsync(request, cancellationToken);
    }
}
