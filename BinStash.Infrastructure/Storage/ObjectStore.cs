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

using BinStash.Contracts.Hashing;
using BinStash.Core.Storage.Stats;
using BinStash.Infrastructure.Storage.FileDefinition;
using BinStash.Infrastructure.Storage.Indexing;
using BinStash.Infrastructure.Storage.Packing;
using BinStash.Infrastructure.Storage.Stats;
using Blake3;

namespace BinStash.Infrastructure.Storage;

/// <summary>
/// High-level API for chunk, file-definition, and release-package storage.
/// </summary>
public class ObjectStore : IDisposable, IAsyncDisposable
{
    private const long MaxPackSize = 4L * 1024 * 1024 * 1024; // 4 GiB max pack file size
    
    private readonly string _basePath;
    private readonly AsyncLruHandlerCache<HandlerCacheKey> _handlerCache;

    public ObjectStore(string basePath, int maxOpenHandlers = 256)
    {
        if (string.IsNullOrEmpty(basePath))
            throw new ArgumentException("Storage directory cannot be null or empty.", nameof(basePath));
        
        Directory.CreateDirectory(basePath);
        _basePath = basePath;

        _handlerCache = new AsyncLruHandlerCache<HandlerCacheKey>(maxOpenHandlers, CreateHandler);
    }

    private IndexedPackFileHandler CreateHandler(HandlerCacheKey key)
    {
        return key.Category switch
        {
            "chunks" => new IndexedPackFileHandler(Path.Combine(_basePath, "Chunks", key.Prefix[..2]), "chunks", key.Prefix, MaxPackSize, ComputeHash),

            "fileDefs" => new IndexedPackFileHandler(Path.Combine(_basePath, "FileDefs", key.Prefix[..2]), "fileDefs", key.Prefix, MaxPackSize, ComputeFileDefHash),

            _ => throw new NotSupportedException($"Unknown handler category '{key.Category}'.")
        };
    }
    
    private ValueTask<HandlerLease> AcquireChunkHandlerAsync(string prefix, CancellationToken ct = default)
        => _handlerCache.AcquireAsync(new HandlerCacheKey("chunks", prefix), ct);

    private ValueTask<HandlerLease> AcquireFileDefHandlerAsync(string prefix, CancellationToken ct = default)
        => _handlerCache.AcquireAsync(new HandlerCacheKey("fileDefs", prefix), ct);
    
    public async Task<bool> RebuildStorageAsync()
    {
        // Phase 1: relocate any file-def entries that are in the wrong prefix bucket
        // (can happen when upgrading from the old StorageKey-keyed store to FileHash-keyed).
        await RepackFileDefsAsync(CancellationToken.None).ConfigureAwait(false);

        using var throttler = new SemaphoreSlim(Environment.ProcessorCount);
        var tasks = new List<Task<bool>>(8192);

        for (var i = 0; i < 4096; i++)
        {
            var prefix = i.ToString("x3");
            tasks.Add(RunRebuildAsync("chunks", prefix, throttler));
            tasks.Add(RunRebuildAsync("fileDefs", prefix, throttler));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.All(static x => x);
    }
    
    public async Task<bool> RebuildStorageWithProgressAsync(IProgress<bool> progress, CancellationToken cancellationToken)
    {
        // Phase 1: relocate any file-def entries that are in the wrong prefix bucket.
        // Progress is not reported for this phase (it is a prerequisite scan, not a per-bucket step).
        await RepackFileDefsAsync(cancellationToken).ConfigureAwait(false);

        using var throttler = new SemaphoreSlim(Environment.ProcessorCount);
        var allSucceeded = true;

        for (var i = 0; i < 4096; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prefix = i.ToString("x3");

            // Run both categories for this prefix before advancing, so progress is per-prefix-pair
            var chunkOk = await RunRebuildAsync("chunks", prefix, throttler).ConfigureAwait(false);
            progress.Report(chunkOk);
            if (!chunkOk) allSucceeded = false;

            cancellationToken.ThrowIfCancellationRequested();

            var fileDefOk = await RunRebuildAsync("fileDefs", prefix, throttler).ConfigureAwait(false);
            progress.Report(fileDefOk);
            if (!fileDefOk) allSucceeded = false;
        }

        return allSucceeded;
    }

    /// <summary>
    /// Scans every FileDef prefix bucket for entries that are stored under the wrong prefix
    /// (i.e. keyed by the old <c>StorageKey = BLAKE3(record blob)</c> instead of the current
    /// <c>FileHash = BLAKE3(file bytes)</c>) and migrates them to the correct bucket.
    ///
    /// <para>
    /// For each misrouted entry the method:
    /// <list type="number">
    ///   <item>Writes the blob into the correct bucket (using <see cref="WriteFileDefinitionAsync"/>,
    ///         which is idempotent — a no-op if already present).</item>
    ///   <item>Rewrites the source bucket's pack files, dropping the now-relocated entry.</item>
    /// </list>
    /// </para>
    /// <para>
    /// This is safe to run against a live store because:
    /// <list type="bullet">
    ///   <item>Every entry is written to the target bucket <em>before</em> it is removed
    ///         from the source bucket, so no data is ever lost.</item>
    ///   <item>The method is idempotent: re-running it on an already-migrated store is a no-op.</item>
    /// </list>
    /// </para>
    /// </summary>
    private async Task RepackFileDefsAsync(CancellationToken cancellationToken)
    {
        var fileDefsRoot = Path.Combine(_basePath, "FileDefs");
        if (!Directory.Exists(fileDefsRoot))
            return;

        using var throttler = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount / 2));

        // Pass 1 — for each bucket, identify misrouted entries and write them to the correct bucket.
        // Collect the set of source prefixes that had at least one misrouted entry (need pack rewrite).
        var dirtyPrefixes = new System.Collections.Concurrent.ConcurrentBag<string>();

        var scanTasks = new List<Task>(4096);
        for (var i = 0; i < 4096; i++)
        {
            var prefix = i.ToString("x3");
            scanTasks.Add(ScanAndRelocateAsync(prefix));
        }

        await Task.WhenAll(scanTasks).ConfigureAwait(false);

        // Pass 2 — for each dirty source bucket, rewrite its pack files, filtering out
        // entries that now belong to a different prefix.
        var rewriteTasks = dirtyPrefixes
            .Distinct(StringComparer.Ordinal)
            .Select(RewriteBucketAsync)
            .ToList();

        await Task.WhenAll(rewriteTasks).ConfigureAwait(false);

        return;

        async Task ScanAndRelocateAsync(string sourcePrefx)
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var bucketDir = Path.Combine(fileDefsRoot, sourcePrefx[..2]);
                var pattern = $"fileDefs{sourcePrefx}-*.pack";

                if (!Directory.Exists(bucketDir))
                    return;

                var packFiles = Directory.EnumerateFiles(bucketDir, pattern).ToArray();
                if (packFiles.Length == 0)
                    return;

                foreach (var packFile in packFiles)
                {
                    await using var fs = new FileStream(
                        packFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 128 * 1024,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                    await foreach (var entry in PackFileEntry.ReadAllEntriesAsync(fs, ignoreChecks: true, cancellationToken).ConfigureAwait(false))
                    {
                        Hash32 fileHash;
                        try
                        {
                            fileHash = FileDefinitionRecord.Deserialize(entry.Data).FileHash;
                        }
                        catch
                        {
                            // Corrupt or unreadable entry — leave it for RebuildIndexFile to handle.
                            continue;
                        }

                        var correctPrefix = fileHash.ToHexString()[..3];
                        if (string.Equals(correctPrefix, sourcePrefx, StringComparison.Ordinal))
                            continue; // Already in the right bucket.

                        // Write to the correct bucket (idempotent).
                        await WriteFileDefinitionAsync(entry.Data.AsMemory()).ConfigureAwait(false);
                        dirtyPrefixes.Add(sourcePrefx);
                    }
                }
            }
            finally
            {
                throttler.Release();
            }
        }

        async Task RewriteBucketAsync(string sourcePrefx)
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var lease = await _handlerCache
                    .AcquireAsync(new HandlerCacheKey("fileDefs", sourcePrefx), cancellationToken)
                    .ConfigureAwait(false);

                // Drop any entry whose FileHash prefix no longer matches this bucket.
                await lease.Handler.RebuildPackFilesAsync(blob =>
                {
                    try
                    {
                        var fh = FileDefinitionRecord.Deserialize(blob).FileHash;
                        return string.Equals(fh.ToHexString()[..3], sourcePrefx, StringComparison.Ordinal);
                    }
                    catch
                    {
                        return true; // Keep corrupt entries for RebuildIndexFile to surface.
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                throttler.Release();
            }
        }
    }

    private async Task<bool> RunRebuildAsync(string category, string prefix, SemaphoreSlim throttler)
    {
        await throttler.WaitAsync().ConfigureAwait(false);
        try
        {
            using var lease = await _handlerCache.AcquireAsync(new HandlerCacheKey(category, prefix)).ConfigureAwait(false);
            return await lease.Handler.RebuildIndexFile().ConfigureAwait(false);
        }
        finally
        {
            throttler.Release();
        }
    }

    public async Task<int> WriteChunkAsync(ReadOnlyMemory<byte> chunkData)
    {
        var hash = ComputeHash(chunkData.Span);
        var stringHash = hash.ToHexString();
        var prefix = stringHash[..3];
        
        using var lease = await AcquireChunkHandlerAsync(prefix).ConfigureAwait(false);
        return await lease.Handler.WriteIndexedDataAsync(hash, chunkData).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadChunkAsync(string hash)
    {
        var prefix = hash[..3];
        using var lease = await AcquireChunkHandlerAsync(prefix).ConfigureAwait(false);
        return await lease.Handler.ReadIndexedDataAsync(Hash32.FromHexString(hash)).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores a serialised <see cref="FileDefinition.FileDefinitionRecord"/> blob in the
    /// file-definition pack store.  The index key is the <c>FileHash</c> embedded inside
    /// the record (BLAKE3 of the original file bytes), enabling direct content-addressed
    /// lookup without a DB round-trip to translate ContentHash → StorageKey.
    /// </summary>
    /// <param name="blob">
    /// Pre-serialised <c>FileDefinitionRecord</c> bytes (produced by
    /// <c>FileDefinitionRecord.Serialize()</c>).
    /// </param>
    /// <returns>
    /// The file hash (BLAKE3 of original file content) and the number of compressed bytes
    /// physically written (0 if the entry already existed).
    /// </returns>
    public async Task<(Hash32 FileHash, int BytesWritten)> WriteFileDefinitionAsync(ReadOnlyMemory<byte> blob)
    {
        var fileHash = FileDefinitionRecord.Deserialize(blob.Span).FileHash;
        var prefix   = fileHash.ToHexString()[..3];
        using var lease = await AcquireFileDefHandlerAsync(prefix).ConfigureAwait(false);
        var written = await lease.Handler.WriteIndexedDataAsync(fileHash, blob).ConfigureAwait(false);
        return (fileHash, written);
    }

    /// <summary>
    /// Retrieves the raw <c>FileDefinitionRecord</c> blob by its file-content hash
    /// (BLAKE3 of the original file bytes).
    /// </summary>
    public async Task<byte[]> ReadFileDefinitionBlobAsync(Hash32 fileHash)
    {
        var prefix = fileHash.ToHexString()[..3];
        using var lease = await AcquireFileDefHandlerAsync(prefix).ConfigureAwait(false);
        return await lease.Handler.ReadIndexedDataAsync(fileHash).ConfigureAwait(false);
    }

    // Keep the string-key overload for convenience (used by LocalFolderChunkStoreStorage).
    public Task<byte[]> ReadFileDefinitionBlobAsync(string fileHashHex)
        => ReadFileDefinitionBlobAsync(Hash32.FromHexString(fileHashHex));

    public async Task WriteReleasePackageAsync(ReadOnlyMemory<byte> releasePackageData)
    {
        var hash = ComputeHash(releasePackageData.Span).ToHexString();
        var folder = Path.Join(_basePath, "Releases", hash[..3]);
        Directory.CreateDirectory(folder);
        var filePath = Path.Join(folder, $"{hash}.rdef");
        await File.WriteAllBytesAsync(filePath, releasePackageData);
    }

    public async Task<byte[]> ReadReleasePackageAsync(string hash)
    {
        var folder = Path.Join(_basePath, "Releases", hash[..3]);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException(folder);
        var filePath = Path.Join(folder, $"{hash}.rdef");
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);
        return await File.ReadAllBytesAsync(filePath);
    }
    
    public Task<bool> DeleteReleasePackageAsync(string hash)
    {
        var folder = Path.Join(_basePath, "Releases", hash[..3]);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException(folder);
        var filePath = Path.Join(folder, $"{hash}.rdef");
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);
        File.Delete(filePath);
        return Task.FromResult(true);
    }
    
    private static Hash32 ComputeHash(ReadOnlySpan<byte> data)
    {
        var hash = Hasher.Hash(data);
        return new Hash32(hash.AsSpan());
    }

    /// <summary>
    /// Hash function used for the file-definition category index.
    /// Extracts the <c>FileHash</c> field from the embedded <see cref="FileDefinitionRecord"/>
    /// so that the pack-store index is keyed by file-content identity.
    /// </summary>
    private static Hash32 ComputeFileDefHash(ReadOnlySpan<byte> data)
        => FileDefinitionRecord.Deserialize(data).FileHash;
    
    public Task<ChunkStorePhysicalStats> GetPhysicalStatsAsync()
    {
        long chunkPackBytes = 0;
        long fileDefinitionPackBytes = 0;
        long releasePackageBytes = 0;
        long indexBytes = 0;

        var chunkPackFileCount = 0;
        var fileDefinitionPackFileCount = 0;
        var releasePackageFileCount = 0;
        var indexFileCount = 0;

        if (Directory.Exists(_basePath))
        {
            foreach (var filePath in Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories))
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name;
                var normalizedPath = filePath.Replace('\\', '/');

                // LSM-tree index files: sorted segment indexes (.seg-NNN.idx),
                // bloom filter sidecars (.seg-NNN.bloom), and Tier-0 append logs (.log).
                if (fileName.EndsWith(".idx", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".bloom", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    indexBytes += fileInfo.Length;
                    indexFileCount++;
                    continue;
                }

                if (fileName.EndsWith(".rdef", StringComparison.OrdinalIgnoreCase))
                {
                    releasePackageBytes += fileInfo.Length;
                    releasePackageFileCount++;
                    continue;
                }

                if (fileName.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                {
                    if (normalizedPath.Contains("/Chunks/", StringComparison.OrdinalIgnoreCase))
                    {
                        chunkPackBytes += fileInfo.Length;
                        chunkPackFileCount++;
                    }
                    else if (normalizedPath.Contains("/FileDefs/", StringComparison.OrdinalIgnoreCase))
                    {
                        fileDefinitionPackBytes += fileInfo.Length;
                        fileDefinitionPackFileCount++;
                    }
                }
            }
        }

        var root = Path.GetPathRoot(Path.GetFullPath(_basePath));
        long volumeTotalBytes = 0;
        long volumeFreeBytes = 0;

        if (!string.IsNullOrWhiteSpace(root))
        {
            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                volumeTotalBytes = drive.TotalSize;
                volumeFreeBytes = drive.AvailableFreeSpace;
            }
        }

        var result = new ChunkStorePhysicalStats
        {
            ChunkPackBytes = chunkPackBytes,
            FileDefinitionPackBytes = fileDefinitionPackBytes,
            ReleasePackageBytes = releasePackageBytes,
            IndexBytes = indexBytes,
            PhysicalBytesTotal = chunkPackBytes + fileDefinitionPackBytes + releasePackageBytes + indexBytes,

            ChunkPackFileCount = chunkPackFileCount,
            FileDefinitionPackFileCount = fileDefinitionPackFileCount,
            ReleasePackageFileCount = releasePackageFileCount,
            IndexFileCount = indexFileCount,

            VolumeTotalBytes = volumeTotalBytes,
            VolumeFreeBytes = volumeFreeBytes
        };

        return Task.FromResult(result);
    }
    
    private const int StatsConcurrency = 32;

    /// <summary>
    /// Returns chunk storage statistics by reading only segment file headers
    /// (8 bytes per file) and the log file size — no handler open required.
    /// At 4096 buckets × ~8 bytes = 32 KB of I/O maximum.
    /// </summary>
    public async Task<StorageStatistics> GetStatisticsAsync()
    {
        using var throttler = new SemaphoreSlim(StatsConcurrency, StatsConcurrency);

        var tasks = new Task<(string prefix, int chunks, int files)>[4096];

        for (var i = 0; i < 4096; i++)
        {
            var prefix = i.ToString("x3");
            tasks[i] = CollectPrefixStatsLightAsync(prefix, throttler);
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var stats        = new StorageStatistics();
        var prefixCounts = new Dictionary<string, int>(4096);

        foreach (var (prefix, chunks, files) in results)
        {
            stats.TotalChunks += chunks;
            stats.TotalFiles  += files;
            prefixCounts[prefix] = chunks;
        }

        stats.PrefixChunkCounts = prefixCounts;
        return stats;
    }

    /// <summary>
    /// Lightweight prefix stats: counts chunk entries by reading 8-byte
    /// segment headers and the byte length of the log file (which is a
    /// conservative upper bound on log entry count, exact only when the
    /// log contains fixed-size records — here we use file length / minimum
    /// varint record size as an approximation, or 0 if the log is absent).
    ///
    /// For an exact log count without opening a handler, we read the log
    /// file via a dedicated counter that replays only hash bytes.
    /// </summary>
    private async Task<(string prefix, int chunks, int files)>
        CollectPrefixStatsLightAsync(string prefix, SemaphoreSlim throttler)
    {
        await throttler.WaitAsync().ConfigureAwait(false);
        try
        {
            var bucketDir = Path.Combine(_basePath, "Chunks", prefix[..2]);

            if (!Directory.Exists(bucketDir))
                return (prefix, 0, 0);

            // 1. Sum entry counts from all segment headers (8 bytes per file)
            var segCount = 0;
            var dataPrefix  = $"chunks{prefix}";
            foreach (var segPath in Directory.EnumerateFiles(bucketDir, $"{dataPrefix}.seg-*.idx"))
                segCount += SortedIndexSegment.ReadEntryCountFromHeader(segPath);

            // 2. Count log entries (replay hash reads without loading a handler)
            var logPath   = Path.Combine(bucketDir, $"{dataPrefix}.log");
            var logCount  = CountLogEntries(logPath);

            // 3. Count pack files
            var packFiles   = Directory.EnumerateFiles(bucketDir, $"{dataPrefix}-*.pack").Count();

            return (prefix, segCount + logCount, packFiles);
        }
        finally
        {
            throttler.Release();
        }
    }

    /// <summary>
    /// Counts entries in an append log by scanning hash bytes (32 bytes per
    /// entry) and skipping the variable-length varint fields.
    /// Returns 0 if the log does not exist or is corrupt.
    /// </summary>
    private static int CountLogEntries(string logPath)
    {
        if (!File.Exists(logPath))
            return 0;

        try
        {
            using var fs     = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

            var count = 0;
            while (fs.Position < fs.Length)
            {
                var hashBytes = reader.ReadBytes(32);
                if (hashBytes.Length < 32)
                    break;

                // Skip the three varint fields (fileNo, offset, length)
                SkipVarInt(reader);
                SkipVarInt(reader);
                SkipVarInt(reader);

                count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static void SkipVarInt(BinaryReader reader)
    {
        // A varint ends at the first byte with the high bit clear.
        for (var i = 0; i < 10; i++)
        {
            var b = reader.ReadByte();
            if ((b & 0x80) == 0)
                return;
        }
        // Malformed — stop gracefully
    }
    
    public void Dispose() => _handlerCache.DisposeAsync().AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync() => _handlerCache.DisposeAsync();
}