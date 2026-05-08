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

using Amazon.S3;
using Amazon.S3.Model;
using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Storage.S3;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace BinStash.Infrastructure.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="S3ChunkStoreStorage"/> using a mocked <see cref="IAmazonS3"/>.
/// All tests use an injected client and a temp-based local cache path.
/// </summary>
public sealed class S3ChunkStoreStorageTests : IDisposable
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();
    private readonly S3BackendSettings _settings;
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly string _localCachePath;

    public S3ChunkStoreStorageTests()
    {
        _localCachePath = Path.Combine(Path.GetTempPath(), "binstash-tests", _storeId.ToString("N"));
        Directory.CreateDirectory(_localCachePath);

        _settings = new S3BackendSettings
        {
            BucketName = "test-bucket",
            Prefix = "test/",
            LocalCachePath = _localCachePath,
            // Use a tiny pack size so tests can trigger rotation without huge data.
            MaxPackSizeBytes = 1024 * 1024,
            MultipartPartSizeBytes = 5 * 1024 * 1024,
        };

        // Default: S3 GetObject for index.bin returns NoSuchKey (new store).
        _s3.GetObjectAsync(Arg.Any<string>(), Arg.Is<string>(k => k.EndsWith("index.bin")))
            .ThrowsAsync(MakeNoSuchKeyException());

        // Default: LIST pending markers returns empty.
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>())
            .Returns(new ListObjectsV2Response { S3Objects = [], IsTruncated = false });
    }

    public void Dispose()
    {
        try { Directory.Delete(_localCachePath, recursive: true); } catch { /* best-effort */ }
    }

    // ── Test 1: StoreChunkAsync with new chunk buffers locally, no immediate S3 PUT ──────────

    [Fact]
    public async Task StoreChunkAsync_NewChunk_BuffersLocallyWithoutImmediateS3Put()
    {
        await using var sut = MakeSut();

        var (hexHash, data) = MakeChunk(512);
        var (success, bytesWritten) = await sut.StoreChunkAsync(hexHash, data);

        success.Should().BeTrue();
        bytesWritten.Should().BeGreaterThan(0);

        // No PUT for a chunk (only PutObject calls that are NOT for the index or marker).
        await _s3.DidNotReceive().PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.Key.EndsWith(".pack")),
            Arg.Any<CancellationToken>());
    }

    // ── Test 2: StoreChunkAsync with duplicate hash is a no-op ───────────────────────────────

    [Fact]
    public async Task StoreChunkAsync_SameHashTwice_SecondCallIsNoOp()
    {
        await using var sut = MakeSut();

        var (hexHash, data) = MakeChunk(512);
        var (_, firstBytes) = await sut.StoreChunkAsync(hexHash, data);
        var (success, secondBytes) = await sut.StoreChunkAsync(hexHash, data);

        success.Should().BeTrue();
        secondBytes.Should().Be(0, "duplicate store must be a no-op returning 0 bytes written");
        firstBytes.Should().BeGreaterThan(0);
    }

    // ── Test 3: RetrieveChunkAsync for chunk in pending write buffer ──────────────────────────

    [Fact]
    public async Task RetrieveChunkAsync_ChunkInPendingBuffer_ReturnedFromLocalFile()
    {
        await using var sut = MakeSut();

        var (hexHash, data) = MakeChunk(512);
        await sut.StoreChunkAsync(hexHash, data);

        var retrieved = await sut.RetrieveChunkAsync(hexHash);

        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(data.ToArray());

        // Must NOT have issued any S3 GetObject for the pack.
        await _s3.DidNotReceive().GetObjectAsync(
            Arg.Is<GetObjectRequest>(r => r.Key.EndsWith(".pack")));
    }

    // ── Test 4: RetrieveChunkAsync for flushed chunk uses ranged S3 GET ──────────────────────

    [Fact]
    public async Task RetrieveChunkAsync_FlushedChunk_IssuesRangedGetFromS3()
    {
        // Arrange: override S3 index to contain one pre-flushed entry.
        var packKey = "test/chunks/aabbcc.pack";
        var (hexHash, originalData) = MakeChunk(64);

        // Build a real pack entry in memory so S3 returns the correct bytes.
        using var packMs = new MemoryStream();
        var (offset, length) = await BinStash.Infrastructure.Storage.Packing.PackFileEntry.WriteAsync(packMs, originalData);
        var packBytes = packMs.ToArray();

        // Stub: index.bin returns serialized index containing our entry.
        _s3.GetObjectAsync(Arg.Any<string>(), Arg.Is<string>(k => k.EndsWith("index.bin")))
            .Returns(callInfo => Task.FromResult(BuildGetObjectResponse(SerializeIndex(hexHash, packKey, offset, length))));

        // Stub: GetObject with ByteRange returns the relevant pack bytes.
        _s3.GetObjectAsync(Arg.Is<GetObjectRequest>(r => r.Key == packKey))
            .Returns(callInfo =>
            {
                var req = callInfo.Arg<GetObjectRequest>();
                var from = (int)req.ByteRange.Start;
                var to = (int)req.ByteRange.End + 1;
                var slice = packBytes[from..to];
                return Task.FromResult(BuildGetObjectResponseFromBytes(slice));
            });

        await using var sut = MakeSut();

        // Act
        var retrieved = await sut.RetrieveChunkAsync(hexHash);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(originalData.ToArray());

        await _s3.Received(1).GetObjectAsync(Arg.Is<GetObjectRequest>(r => r.Key == packKey));
    }

    // ── Test 5: DisposeAsync flushes pending pack via PutObject/multipart ────────────────────

    [Fact]
    public async Task DisposeAsync_WithPendingChunks_FlushesPackToS3()
    {
        // Arrange: stub PutObject calls (marker + pack + index).
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        _s3.DeleteObjectAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new DeleteObjectResponse());

        var sut = MakeSut();
        var (hexHash, data) = MakeChunk(128);
        await sut.StoreChunkAsync(hexHash, data);

        // Act
        await sut.DisposeAsync();

        // Assert: at minimum one PutObject call for the pack (small file → PutObject) and one for the index.
        await _s3.Received().PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.Key.EndsWith(".pack")),
            Arg.Any<CancellationToken>());
        await _s3.Received().PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.Key.EndsWith("index.bin")),
            Arg.Any<CancellationToken>());
    }

    // ── Test 6: StoreFileDefinitionAsync PUTs individual object ──────────────────────────────

    [Fact]
    public async Task StoreFileDefinitionAsync_PutsIndividualS3Object()
    {
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());

        await using var sut = MakeSut();

        var hashBytes = new byte[32];
        new Random(42).NextBytes(hashBytes);
        var fileHash = new Hash32(hashBytes);
        var data = new byte[256];
        new Random(1).NextBytes(data);

        var (success, bytesWritten) = await sut.StoreFileDefinitionAsync(fileHash, data);

        success.Should().BeTrue();
        bytesWritten.Should().Be(256);

        await _s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.Key.Contains("filedefs/")),
            Arg.Any<CancellationToken>());
    }

    // ── Test 7: StoreReleasePackageAsync PUTs individual object at releases/ ─────────────────

    [Fact]
    public async Task StoreReleasePackageAsync_PutsIndividualS3Object()
    {
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());

        await using var sut = MakeSut();

        var packageData = new byte[512];
        new Random(7).NextBytes(packageData);

        var success = await sut.StoreReleasePackageAsync(packageData);

        success.Should().BeTrue();
        await _s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.Key.Contains("releases/")),
            Arg.Any<CancellationToken>());
    }

    // ── Test 8: RetrieveFileDefinitionsAsync uses bounded parallelism ──────────────────────

    [Fact]
    public async Task RetrieveFileDefinitionsAsync_BatchOf10_ReturnsAllFoundItems()
    {
        var fileData = new Dictionary<string, byte[]>();
        for (var i = 0; i < 10; i++)
        {
            var hash = new string('0', 62) + i.ToString("D2");
            var data = new byte[64];
            data[0] = (byte)i;
            fileData[hash] = data;
        }

        _s3.GetObjectAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                var matchHash = fileData.Keys.FirstOrDefault(h => key.Contains(h));
                if (matchHash != null)
                    return Task.FromResult(BuildGetObjectResponseFromBytes(fileData[matchHash]));
                throw MakeNoSuchKeyException();
            });

        await using var sut = MakeSut();

        var result = await sut.RetrieveFileDefinitionsAsync(fileData.Keys.ToList());

        result.Should().HaveCount(10);
    }

    // ── Test 9: RebuildStorageAsync throws NotSupportedException ─────────────────────────────

    [Fact]
    public async Task RebuildStorageAsync_ThrowsNotSupportedException()
    {
        await using var sut = MakeSut();

        var act = () => sut.RebuildStorageAsync();

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // ── Test 10: GetPhysicalStatsAsync returns plausible values without S3 LIST ──────────────

    [Fact]
    public async Task GetPhysicalStatsAsync_AfterStoreChunk_ReturnsNonZeroStats()
    {
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        _s3.DeleteObjectAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new DeleteObjectResponse());

        await using var sut = MakeSut();

        var (hexHash, data) = MakeChunk(512);
        await sut.StoreChunkAsync(hexHash, data);

        var stats = await sut.GetPhysicalStatsAsync();

        stats.ChunkPackBytes.Should().BeGreaterThan(0, "pending pack bytes should be reflected immediately");
        stats.PhysicalBytesTotal.Should().BeGreaterThan(0);

        // Must NOT have issued any LIST calls (stats must come from counters).
        await _s3.DidNotReceive().ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(r => !r.Prefix.Contains("pending/")));
    }

    // ── Test 11: RetrieveChunkAsync for unknown chunk returns null ────────────────────────────

    [Fact]
    public async Task RetrieveChunkAsync_UnknownChunk_ReturnsNull()
    {
        await using var sut = MakeSut();

        var result = await sut.RetrieveChunkAsync(new string('f', 64));

        result.Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private S3ChunkStoreStorage MakeSut()
        => new(_settings, _storeId, _s3);

    private static (string HexHash, ReadOnlyMemory<byte> Data) MakeChunk(int size)
    {
        var data = new byte[size];
        new Random(size).NextBytes(data);
        // Compute a deterministic "hash" for the test (not real BLAKE3 — that's fine for unit tests).
        var hash = System.Security.Cryptography.SHA256.HashData(data);
        // Pad to 32 bytes and hex-encode to 64 chars.
        var hex = Convert.ToHexStringLower(hash);
        return (hex, data.AsMemory());
    }

    private static AmazonS3Exception MakeNoSuchKeyException()
        => new("NoSuchKey", Amazon.Runtime.ErrorType.Receiver, "NoSuchKey", null, System.Net.HttpStatusCode.NotFound)
        {
            // ErrorCode property is set via the constructor argument.
        };

    private static GetObjectResponse BuildGetObjectResponse(byte[] data)
    {
        var response = new GetObjectResponse();
        SetResponseStream(response, data);
        return response;
    }

    private static GetObjectResponse BuildGetObjectResponseFromBytes(byte[] data)
        => BuildGetObjectResponse(data);

    private static void SetResponseStream(GetObjectResponse response, byte[] data)
    {
        // ResponseStream is get-only in AWS SDK — use reflection to set it for testing.
        var ms = new MemoryStream(data);
        var prop = typeof(GetObjectResponse).GetProperty("ResponseStream");
        if (prop?.CanWrite == true)
        {
            prop.SetValue(response, ms);
        }
        else
        {
            // Fallback: use the internal field if the property is backed by a field.
            var field = typeof(GetObjectResponse).GetField("_responseStream",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? typeof(Amazon.Runtime.AmazonWebServiceResponse).GetField("_responseStream",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(response, ms);
        }
    }

    /// <summary>
    /// Builds a minimal serialized S3 index binary containing a single entry.
    /// </summary>
    private static byte[] SerializeIndex(string hexHash, string packKey, long offset, int length)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        // Magic: "S3IX" = 0x58493353 LE
        w.Write((uint)0x58493353u);
        // Version
        w.Write((byte)1);
        // Count
        w.Write((int)1);

        // Hash: 32 bytes binary
        var hashBytes = Convert.FromHexString(hexHash);
        ms.Write(hashBytes);

        // PackKey: 2-byte length + UTF8
        var packKeyBytes = System.Text.Encoding.UTF8.GetBytes(packKey);
        w.Write((ushort)packKeyBytes.Length);
        ms.Write(packKeyBytes);

        // Offset: 8 bytes LE
        w.Write((long)offset);
        // Length: 4 bytes LE
        w.Write((int)length);

        w.Flush();
        return ms.ToArray();
    }
}
