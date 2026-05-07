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
using BinStash.Infrastructure.Storage.Indexing;
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
            "chunks" => new IndexedPackFileHandler(
                Path.Combine(_basePath, "Chunks", key.Prefix[..2]),
                "chunks",
                key.Prefix,
                MaxPackSize,
                ComputeHash),

            "fileDefs" => new IndexedPackFileHandler(
                Path.Combine(_basePath, "FileDefs", key.Prefix[..2]),
                "fileDefs",
                key.Prefix,
                MaxPackSize,
                ComputeHash),

            _ => throw new NotSupportedException($"Unknown handler category '{key.Category}'.")
        };
    }
    
    private ValueTask<HandlerLease> AcquireChunkHandlerAsync(string prefix, CancellationToken ct = default)
        => _handlerCache.AcquireAsync(new HandlerCacheKey("chunks", prefix), ct);

    private ValueTask<HandlerLease> AcquireFileDefHandlerAsync(string prefix, CancellationToken ct = default)
        => _handlerCache.AcquireAsync(new HandlerCacheKey("fileDefs", prefix), ct);
    
    public async Task<bool> RebuildStorageAsync()
    {
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
    
    public async Task<int> WriteFileDefinitionAsync(Hash32 fileHash, ReadOnlyMemory<byte> fileDefinitionData)
    {
        var stringHash = fileHash.ToHexString();
        var prefix = stringHash[..3];
        using var lease = await AcquireFileDefHandlerAsync(prefix).ConfigureAwait(false);
        return await lease.Handler.WriteIndexedDataAsync(fileHash, fileDefinitionData).ConfigureAwait(false);
    }
    
    public async Task<byte[]> ReadFileDefinitionAsync(string hash)
    {
        var prefix = hash[..3];
        using var lease = await AcquireFileDefHandlerAsync(prefix).ConfigureAwait(false);
        return await lease.Handler.ReadIndexedDataAsync(Hash32.FromHexString(hash)).ConfigureAwait(false);
    }

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

                if (fileName.EndsWith(".idx", StringComparison.OrdinalIgnoreCase))
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
    
    public async Task<StorageStatistics> GetStatisticsAsync()
    {
        var stats = new StorageStatistics();
        var prefixCounts = new Dictionary<string, int>();

        for (var i = 0; i < 4096; i++)
        {
            var prefix = i.ToString("x3");

            using var lease = await AcquireChunkHandlerAsync(prefix).ConfigureAwait(false);
            var handler = lease.Handler;
            var index = handler.GetIndexSnapshot();

            stats.TotalChunks += index.Count;
            prefixCounts[prefix] = index.Count;

            foreach (var entry in index.Values)
            {
                stats.TotalCompressedSize += entry.length;
                stats.TotalUncompressedSize += handler.GetEstimatedUncompressedSize(entry);
            }

            stats.TotalFiles += handler.CountDataFiles();
        }

        stats.PrefixChunkCounts = prefixCounts;
        return stats;
    }
    
    public void Dispose() => _handlerCache.DisposeAsync().AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync() => _handlerCache.DisposeAsync();
}