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

using System.Buffers.Binary;
using System.Collections.Concurrent;
using Amazon.S3;
using Amazon.S3.Model;
using Blake3;
using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Core.Storage;
using BinStash.Core.Storage.Stats;

namespace BinStash.Infrastructure.Storage.S3;

/// <summary>
/// S3-compatible chunk store backend that batches chunks into pack files before uploading,
/// keeping per-ingest-session PUT counts minimal (typically 1 pack + 1 index update).
/// </summary>
/// <remarks>
/// <para><b>Write path (chunks):</b> chunks are buffered locally into a pack file. Once the pack
/// reaches <see cref="S3BackendSettings.MaxPackSizeBytes"/> or the store is disposed, the pack
/// is uploaded to S3 via multipart upload and the in-memory index is persisted.</para>
/// <para><b>Read path (chunks):</b> the in-memory index is consulted; if the chunk is in the
/// current write buffer it is read from the local temp file; if flushed, a single S3 ranged
/// GET retrieves just the pack entry bytes.</para>
/// <para><b>Crash safety:</b> before uploading a pack, a write-ahead marker object is stored in S3.
/// On startup, any leftover markers are reconciled — packs that were fully uploaded but whose
/// index update was lost are recovered; incomplete multipart uploads are aborted.</para>
/// </remarks>
public sealed class S3ChunkStoreStorage : IChunkStoreStorage, IAsyncDisposable
{
    // ── S3 key helpers ───────────────────────────────────────────────────────

    private string IndexKey => $"{_settings.Prefix}chunks/index.bin";
    private string PendingPrefix => $"{_settings.Prefix}chunks/pending/";

    private string FileDefKey(string hexHash) =>
        $"{_settings.Prefix}filedefs/{hexHash[..4]}/{hexHash}.bin";

    private string ReleaseKey(string hexHash) =>
        $"{_settings.Prefix}releases/{hexHash[..4]}/{hexHash}.rdef";

    // ── State ────────────────────────────────────────────────────────────────

    private readonly S3BackendSettings _settings;
    private readonly IAmazonS3 _client;
    private readonly bool _ownsClient;
    private readonly string _localCachePath;
    private readonly S3PackIndex _index;
    private S3PackWriter? _currentWriter;

    /// <summary>Serializes all writes and pack rotations. Held during pending-read to prevent temp file deletion races.</summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private int _disposed;

    private readonly Task _initTask;

    // ── Stats (approximate — atomic counters updated on store operations) ────

    private long _chunkPackBytesTotal;
    private long _fileDefBytesTotal;
    private long _releasePackBytesTotal;
    private long _chunkPackFileCount;
    private long _fileDefFileCount;
    private long _releaseFileCount;
    private long _lastIndexSizeBytes;

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Creates an S3 chunk store storage using credentials/endpoint from <paramref name="settings"/>.</summary>
    public S3ChunkStoreStorage(S3BackendSettings settings, Guid storeId)
        : this(settings, storeId, S3ClientFactory.Create(settings), ownsClient: true)
    {
    }

    /// <summary>Internal constructor that accepts an injected <see cref="IAmazonS3"/> client (for unit tests).</summary>
    internal S3ChunkStoreStorage(S3BackendSettings settings, Guid storeId, IAmazonS3 client)
        : this(settings, storeId, client, ownsClient: false)
    {
    }

    private S3ChunkStoreStorage(S3BackendSettings settings, Guid storeId, IAmazonS3 client, bool ownsClient)
    {
        _settings = settings;
        _client = client;
        _ownsClient = ownsClient;
        _localCachePath = settings.LocalCachePath
            ?? Path.Combine(Path.GetTempPath(), "binstash-s3", storeId.ToString("N"));
        _index = new S3PackIndex();

        Directory.CreateDirectory(_localCachePath);

        _initTask = InitializeAsync();
    }

    // ── Initialization & crash recovery ──────────────────────────────────────

    private async Task InitializeAsync()
    {
        // Clean local orphaned temp files from previous crash.
        CleanupLocalTempFiles();

        // Load the persisted chunk index from S3.
        await _index.LoadFromS3Async(_client, _settings.BucketName, IndexKey).ConfigureAwait(false);

        // Recover from any crash that occurred between pack upload and index save.
        await RecoverPendingPacksAsync().ConfigureAwait(false);
    }

    private void CleanupLocalTempFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_localCachePath, "*.pack.tmp"))
            {
                try { File.Delete(file); }
                catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Scans for write-ahead pending marker objects in S3 left by a previous crash and reconciles them.
    /// </summary>
    /// <remarks>
    /// A marker is written before pack upload and deleted only after the index is successfully saved.
    /// On recovery:
    /// <list type="bullet">
    ///   <item>Pack exists in S3 → merge entries into index and resave.</item>
    ///   <item>Pack missing (crash during upload) → abort any orphaned multipart upload and delete marker.</item>
    /// </list>
    /// </remarks>
    private async Task RecoverPendingPacksAsync()
    {
        List<PendingMarker> markers;
        try
        {
            markers = await ListPendingMarkersAsync().ConfigureAwait(false);
        }
        catch
        {
            // If we can't list markers (e.g. bucket doesn't exist yet), skip recovery silently.
            return;
        }

        foreach (var marker in markers)
        {
            try
            {
                await RecoverOneMarkerAsync(marker).ConfigureAwait(false);
            }
            catch
            {
                // Log recovery failure but do not abort startup — the store is still usable,
                // just those specific chunks may be re-uploaded on next ingest.
            }
        }
    }

    private async Task<List<PendingMarker>> ListPendingMarkersAsync()
    {
        var result = new List<PendingMarker>();
        var request = new ListObjectsV2Request
        {
            BucketName = _settings.BucketName,
            Prefix = PendingPrefix,
        };

        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request).ConfigureAwait(false);
            foreach (var obj in response.S3Objects)
            {
                try
                {
                    var markerResponse = await _client.GetObjectAsync(_settings.BucketName, obj.Key).ConfigureAwait(false);
                    await using var stream = markerResponse.ResponseStream;
                    var marker = await PendingMarker.DeserializeAsync(obj.Key, stream).ConfigureAwait(false);
                    result.Add(marker);
                }
                catch { /* skip corrupt markers */ }
            }
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated);

        return result;
    }

    private async Task RecoverOneMarkerAsync(PendingMarker marker)
    {
        // Check if the pack object actually made it to S3.
        bool packExists;
        try
        {
            await _client.GetObjectMetadataAsync(_settings.BucketName, marker.PackKey).ConfigureAwait(false);
            packExists = true;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NotFound" || ex.ErrorCode == "NoSuchKey")
        {
            packExists = false;
        }

        if (packExists)
        {
            // The pack uploaded successfully but the index update was lost.
            // Merge the marker's entries into the index and resave.
            foreach (var (hexHash, offset, length) in marker.Entries)
                _index.Add(hexHash, new ChunkLocation(marker.PackKey, offset, length));

            _lastIndexSizeBytes = await _index.SaveToS3Async(_client, _settings.BucketName, IndexKey).ConfigureAwait(false);

            Interlocked.Increment(ref _chunkPackFileCount);
        }
        else
        {
            // The pack upload was incomplete. Abort any orphaned multipart uploads.
            await AbortOrphanedMultipartAsync(marker.PackKey).ConfigureAwait(false);
        }

        // Delete the marker now that we've handled it.
        await _client.DeleteObjectAsync(_settings.BucketName, marker.MarkerKey).ConfigureAwait(false);
    }

    private async Task AbortOrphanedMultipartAsync(string packKey)
    {
        try
        {
            var request = new ListMultipartUploadsRequest
            {
                BucketName = _settings.BucketName,
                Prefix = packKey,
            };
            var response = await _client.ListMultipartUploadsAsync(request).ConfigureAwait(false);
            foreach (var upload in response.MultipartUploads)
            {
                try
                {
                    await _client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                    {
                        BucketName = _settings.BucketName,
                        Key = upload.Key,
                        UploadId = upload.UploadId,
                    }).ConfigureAwait(false);
                }
                catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }

    private Task EnsureInitializedAsync() => _initTask;

    // ── IChunkStoreStorage — chunk operations ─────────────────────────────────

    /// <inheritdoc/>
    public async Task<(bool Success, int BytesWritten)> StoreChunkAsync(string key, ReadOnlyMemory<byte> data)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        // Fast path: already in flushed index (lock-free ConcurrentDictionary read).
        if (_index.TryGet(key, out _))
            return (true, 0);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock (another thread may have stored while we waited).
            if (_index.TryGet(key, out _))
                return (true, 0);

            if (_currentWriter?.HasEntry(key) == true)
                return (true, 0);

            // Lazily create the writer.
            _currentWriter ??= new S3PackWriter(_localCachePath, _settings.Prefix);

            var bytesWritten = await _currentWriter.WriteEntryAsync(key, data).ConfigureAwait(false);

            // Rotate the pack if it has reached the size threshold.
            if (_currentWriter.CurrentSize >= _settings.MaxPackSizeBytes)
                await FlushCurrentPackAsync().ConfigureAwait(false);

            return (true, bytesWritten);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]?> RetrieveChunkAsync(string key)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        // Fast path: chunk is in the flushed index — ranged GET from S3 (no lock needed).
        if (_index.TryGet(key, out var flushedLoc))
            return await ReadChunkFromS3Async(flushedLoc).ConfigureAwait(false);

        // Slow path: check the pending write buffer.
        // Hold the write lock to prevent the writer from being flushed (and the temp file deleted)
        // while we are reading from it.
        await _writeLock.WaitAsync().ConfigureAwait(false);
        bool lockReleased = false;
        try
        {
            // Re-check flushed index — a flush may have completed while we were waiting for the lock.
            if (_index.TryGet(key, out flushedLoc))
            {
                // Release the write lock before the async S3 read; flushed data is immutable.
                _writeLock.Release();
                lockReleased = true;
                return await ReadChunkFromS3Async(flushedLoc).ConfigureAwait(false);
            }

            if (_currentWriter?.TryGetEntry(key, out var pendingLoc) == true)
            {
                // Read from the local temp file while the lock is held to prevent disposal.
                return await _currentWriter.ReadEntryAtAsync(pendingLoc.Offset).ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            if (!lockReleased)
                _writeLock.Release();
        }
    }

    // ── IChunkStoreStorage — file definition operations ───────────────────────

    /// <inheritdoc/>
    public async Task<(bool Success, int BytesWritten)> StoreFileDefinitionAsync(Hash32 fileHash, ReadOnlyMemory<byte> data)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var hexHash = fileHash.ToHexString();
        var s3Key = FileDefKey(hexHash);

        await PutObjectAsync(s3Key, data).ConfigureAwait(false);

        Interlocked.Add(ref _fileDefBytesTotal, data.Length);
        Interlocked.Increment(ref _fileDefFileCount);

        return (true, data.Length);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> RetrieveFileDefinitionAsync(string key)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetObjectBytesAsync(FileDefKey(key)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, byte[]>> RetrieveFileDefinitionsAsync(IReadOnlyCollection<string> fileHashes)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await RetrieveBatchAsync(fileHashes, RetrieveFileDefinitionAsync).ConfigureAwait(false);
    }

    // ── IChunkStoreStorage — release package operations ───────────────────────

    /// <inheritdoc/>
    public async Task<bool> StoreReleasePackageAsync(ReadOnlyMemory<byte> packageData)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var hexHash = ComputeBlake3Hash(packageData.Span).ToHexString();
        var s3Key = ReleaseKey(hexHash);

        await PutObjectAsync(s3Key, packageData).ConfigureAwait(false);

        Interlocked.Add(ref _releasePackBytesTotal, packageData.Length);
        Interlocked.Increment(ref _releaseFileCount);

        return true;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> RetrieveReleasePackageAsync(string key)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await GetObjectBytesAsync(ReleaseKey(key)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteReleasePackageAsync(string packageId)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        try
        {
            await _client.DeleteObjectAsync(_settings.BucketName, ReleaseKey(packageId)).ConfigureAwait(false);
            Interlocked.Decrement(ref _releaseFileCount);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey" or "NotFound")
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, byte[]>> RetrieveReleasePackagesAsync(IReadOnlyCollection<string> packageIds)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await RetrieveBatchAsync(packageIds, RetrieveReleasePackageAsync).ConfigureAwait(false);
    }

    // ── IChunkStoreStorage — stats & management ───────────────────────────────

    /// <inheritdoc/>
    public Task<ChunkStorePhysicalStats> GetPhysicalStatsAsync()
    {
        var pendingSize = _currentWriter?.CurrentSize ?? 0L;
        var chunkPackBytes = Interlocked.Read(ref _chunkPackBytesTotal) + pendingSize;
        var fileDefBytes = Interlocked.Read(ref _fileDefBytesTotal);
        var releasePackBytes = Interlocked.Read(ref _releasePackBytesTotal);
        var indexBytes = Interlocked.Read(ref _lastIndexSizeBytes);

        return Task.FromResult(new ChunkStorePhysicalStats
        {
            ChunkPackBytes = chunkPackBytes,
            FileDefinitionPackBytes = fileDefBytes,
            ReleasePackageBytes = releasePackBytes,
            IndexBytes = indexBytes,
            PhysicalBytesTotal = chunkPackBytes + fileDefBytes + releasePackBytes + indexBytes,

            ChunkPackFileCount = (int)Interlocked.Read(ref _chunkPackFileCount),
            FileDefinitionPackFileCount = (int)Interlocked.Read(ref _fileDefFileCount),
            ReleasePackageFileCount = (int)Interlocked.Read(ref _releaseFileCount),
            IndexFileCount = 1, // one index.bin

            VolumeTotalBytes = 0, // not applicable for S3
            VolumeFreeBytes = 0,
        });
    }

    /// <inheritdoc/>
    public Task<bool> RebuildStorageAsync()
        => throw new NotSupportedException(
            "Rebuild is not supported for S3 chunk stores in this version. " +
            "Re-upload content from source or restore from the index.");

    /// <inheritdoc/>
    public async Task<Dictionary<string, object>> GetStorageStatsAsync()
    {
        var stats = await GetPhysicalStatsAsync().ConfigureAwait(false);
        return new Dictionary<string, object>
        {
            ["bucket"] = _settings.BucketName,
            ["prefix"] = _settings.Prefix,
            ["index_chunk_count"] = _index.Count,
            ["chunk_pack_count"] = stats.ChunkPackFileCount,
            ["chunk_pack_bytes"] = stats.ChunkPackBytes,
            ["file_def_count"] = stats.FileDefinitionPackFileCount,
            ["file_def_bytes"] = stats.FileDefinitionPackBytes,
            ["release_count"] = stats.ReleasePackageFileCount,
            ["release_bytes"] = stats.ReleasePackageBytes,
            ["index_bytes"] = stats.IndexBytes,
        };
    }

    // ── Flush (write lock must be held by caller) ─────────────────────────────

    /// <summary>
    /// Uploads the current write pack to S3 and updates the index.
    /// Caller must hold <see cref="_writeLock"/>.
    /// </summary>
    private async Task FlushCurrentPackAsync()
    {
        if (_currentWriter == null || _currentWriter.EntryCount == 0)
            return;

        var writer = _currentWriter;
        var packSize = writer.CurrentSize;

        // ── Write-ahead marker ────────────────────────────────────────────────
        // Persist the marker BEFORE uploading the pack. On a crash after upload but before
        // the index is saved, startup recovery will find this marker and merge the entries.
        var markerKey = $"{PendingPrefix}{Guid.NewGuid():N}.pending";
        var entries = writer.GetEntries();
        await WritePendingMarkerAsync(markerKey, writer.PackKey, entries).ConfigureAwait(false);

        try
        {
            // ── Upload the pack ───────────────────────────────────────────────
            await writer.FlushAndUploadAsync(_client, _settings.BucketName, _settings.MultipartPartSizeBytes)
                .ConfigureAwait(false);

            // ── Update the in-memory index ────────────────────────────────────
            foreach (var (hexHash, (offset, length)) in entries)
                _index.Add(hexHash, new ChunkLocation(writer.PackKey, offset, length));

            // ── Persist the index to S3 ───────────────────────────────────────
            _lastIndexSizeBytes = await _index.SaveToS3Async(_client, _settings.BucketName, IndexKey)
                .ConfigureAwait(false);

            // ── Update stats ──────────────────────────────────────────────────
            Interlocked.Add(ref _chunkPackBytesTotal, packSize);
            Interlocked.Increment(ref _chunkPackFileCount);

            // ── Delete the write-ahead marker (all steps succeeded) ───────────
            try
            {
                await _client.DeleteObjectAsync(_settings.BucketName, markerKey).ConfigureAwait(false);
            }
            catch
            {
                // Non-fatal: the marker will be cleaned up on next startup by RecoverPendingPacksAsync.
            }
        }
        finally
        {
            // Always dispose and clean up the local temp file.
            await writer.DisposeAsync().ConfigureAwait(false);
            _currentWriter = null;
        }
    }

    /// <summary>
    /// Writes a write-ahead pending marker to S3 containing the pack key and all entry locations.
    /// </summary>
    private async Task WritePendingMarkerAsync(
        string markerKey,
        string packKey,
        IReadOnlyDictionary<string, (long Offset, int Length)> entries)
    {
        using var ms = new MemoryStream();
        await PendingMarker.SerializeAsync(ms, packKey, entries).ConfigureAwait(false);
        ms.Position = 0;

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _settings.BucketName,
            Key = markerKey,
            InputStream = ms,
        }).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<byte[]?> ReadChunkFromS3Async(ChunkLocation loc)
    {
        var request = new GetObjectRequest
        {
            BucketName = _settings.BucketName,
            Key = loc.PackKey,
            ByteRange = new ByteRange(loc.Offset, loc.Offset + loc.Length - 1),
        };

        try
        {
            using var response = await _client.GetObjectAsync(request).ConfigureAwait(false);
            using var ms = new MemoryStream(loc.Length);
            await response.ResponseStream.CopyToAsync(ms).ConfigureAwait(false);
            ms.Position = 0;
            return await Packing.PackFileEntry.ReadAsync(ms).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey" or "NotFound")
        {
            return null;
        }
    }

    private async Task PutObjectAsync(string s3Key, ReadOnlyMemory<byte> data)
    {
        var request = new PutObjectRequest
        {
            BucketName = _settings.BucketName,
            Key = s3Key,
            InputStream = new MemoryStream(data.ToArray()),
        };
        await _client.PutObjectAsync(request).ConfigureAwait(false);
    }

    private async Task<byte[]?> GetObjectBytesAsync(string s3Key)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_settings.BucketName, s3Key).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchKey" or "NotFound")
        {
            return null;
        }
    }

    private static async Task<Dictionary<string, byte[]>> RetrieveBatchAsync(
        IReadOnlyCollection<string> keys,
        Func<string, Task<byte[]?>> retrieve)
    {
        var result = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var throttler = new SemaphoreSlim(16);

        await Task.WhenAll(keys.Select(async key =>
        {
            await throttler.WaitAsync().ConfigureAwait(false);
            try
            {
                var data = await retrieve(key).ConfigureAwait(false);
                if (data != null)
                    result[key] = data;
            }
            finally
            {
                throttler.Release();
            }
        })).ConfigureAwait(false);

        return new Dictionary<string, byte[]>(result, StringComparer.OrdinalIgnoreCase);
    }

    private static Hash32 ComputeBlake3Hash(ReadOnlySpan<byte> data)
    {
        var hash = Hasher.Hash(data);
        return new Hash32(hash.AsSpan());
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Ensure initialization has completed (or failed) before we flush.
        try { await _initTask.ConfigureAwait(false); }
        catch { /* ignore init failures during dispose */ }

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_currentWriter is { EntryCount: > 0 })
                await FlushCurrentPackAsync().ConfigureAwait(false);
            else if (_currentWriter is not null)
            {
                await _currentWriter.DisposeAsync().ConfigureAwait(false);
                _currentWriter = null;
            }
        }
        finally
        {
            _writeLock.Release();
        }

        _writeLock.Dispose();
        if (_ownsClient)
            _client.Dispose();
    }
}

// ── Write-ahead pending marker ─────────────────────────────────────────────────

/// <summary>
/// Serializable record of a pack upload that is in progress.
/// Stored in S3 as a binary object before the pack upload begins.
/// Deleted after the index is successfully saved.
/// </summary>
internal sealed class PendingMarker
{
    private const uint Magic = 0x504D4B57; // "WKMP" (WaK-ahead MarKer Pending)
    private const byte Version = 1;

    public string MarkerKey { get; }
    public string PackKey { get; }
    public IReadOnlyList<(string HexHash, long Offset, int Length)> Entries { get; }

    private PendingMarker(string markerKey, string packKey, IReadOnlyList<(string, long, int)> entries)
    {
        MarkerKey = markerKey;
        PackKey = packKey;
        Entries = entries;
    }

    public static async Task SerializeAsync(
        Stream output,
        string packKey,
        IReadOnlyDictionary<string, (long Offset, int Length)> entries)
    {
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(packKey);
        writer.Write(entries.Count);

        foreach (var (hexHash, (offset, length)) in entries)
        {
            writer.Write(hexHash);
            writer.Write(offset);
            writer.Write(length);
        }

        await output.FlushAsync().ConfigureAwait(false);
    }

    public static async Task<PendingMarker> DeserializeAsync(string markerKey, Stream input)
    {
        // Read all bytes so we can use BinaryReader synchronously.
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms).ConfigureAwait(false);
        ms.Position = 0;

        using var reader = new BinaryReader(ms, System.Text.Encoding.UTF8);

        var magic = reader.ReadUInt32();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid pending marker magic: 0x{magic:X8}");

        var version = reader.ReadByte();
        if (version != Version)
            throw new NotSupportedException($"Unsupported pending marker version {version}");

        var packKey = reader.ReadString();
        var count = reader.ReadInt32();
        var entries = new List<(string, long, int)>(count);

        for (var i = 0; i < count; i++)
        {
            var hexHash = reader.ReadString();
            var offset = reader.ReadInt64();
            var length = reader.ReadInt32();
            entries.Add((hexHash, offset, length));
        }

        return new PendingMarker(markerKey, packKey, entries);
    }
}
