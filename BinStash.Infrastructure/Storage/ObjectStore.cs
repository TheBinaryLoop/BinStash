// Copyright (C) 2025  Lukas Eßmann
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
using BinStash.Infrastructure.Storage.Indexing;
using BinStash.Infrastructure.Storage.Stats;
using Blake3;

namespace BinStash.Infrastructure.Storage;

/// <summary>
/// High-level API for chunk, file-definition and release-package storage.
/// </summary>
public class ObjectStore
{
    private const long MaxPackSize = 4L * 1024 * 1024 * 1024; // 4 GiB max pack file size
    
    private readonly string _basePath;
    private readonly Dictionary<string, IndexedPackFileHandler> _chunkFileHandlers = new();
    private readonly Dictionary<string, IndexedPackFileHandler> _fileDefinitionFileHandlers = new();

    public ObjectStore(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
            throw new ArgumentException("Storage directory cannot be null or empty.", nameof(basePath));
        
        Directory.CreateDirectory(basePath);

        _basePath = basePath;
        InitializeFileHandlers();
    }

    private void InitializeFileHandlers()
    {
        for (var i = 0; i < 4096; i++)
        {
            var prefix = i.ToString("x3");
            // The folder structure will look like:
            // basePath/
            //   (Chunks|FileDefs)/
            //     00/
            //       index00x.idx
            //       chunks00x-n.pack
            _chunkFileHandlers[prefix] = new IndexedPackFileHandler(Path.Combine(_basePath, "Chunks", prefix[..2]), "chunks", prefix, MaxPackSize, ComputeHash);
            _fileDefinitionFileHandlers[prefix] = new IndexedPackFileHandler(Path.Combine(_basePath, "FileDefs", prefix[..2]), "fileDefs", prefix, MaxPackSize, ComputeHash);
        }
    }
    
    public async Task<bool> RebuildStorageAsync()
    {
        var tasks = new List<Task<bool>>();
        /*foreach (var handler in _chunkFileHandlers.Values)
        {
            tasks.Add(handler.RebuildPackFilesAsync());
        }
        foreach (var handler in _fileDefinitionFileHandlers.Values)
        {
            tasks.Add(handler.RebuildPackFilesAsync());
        }
        var results = await Task.WhenAll(tasks);
        if (!results.All(r => r))
            return false;
        tasks.Clear();*/
        var results = Array.Empty<bool>();
        foreach (var handler in _chunkFileHandlers.Values)
        {
            tasks.Add(handler.RebuildIndexFile());
        }
        foreach (var handler in _fileDefinitionFileHandlers.Values)
        {
            tasks.Add(handler.RebuildIndexFile());
        }
        results = await Task.WhenAll(tasks);
        
        return results.All(r => r);
    }

    public async Task<int> WriteChunkAsync(byte[] chunkData)
    {
        var hash = ComputeHash(chunkData);
        var stringHash = hash.ToHexString();
        var prefix = stringHash[..3];
        return await _chunkFileHandlers[prefix].WriteIndexedDataAsync(hash, chunkData);
    }

    public async Task<byte[]> ReadChunkAsync(string hash)
    {
        var prefix = hash[..3];
        return await _chunkFileHandlers[prefix].ReadIndexedDataAsync(Hash32.FromHexString(hash));
    }
    
    public async Task<int> WriteFileDefinitionAsync(Hash32 fileHash, byte[] fileDefinitionData)
    {
        var stringHash = fileHash.ToHexString();
        var prefix = stringHash[..3];
        return await _fileDefinitionFileHandlers[prefix].WriteIndexedDataAsync(fileHash, fileDefinitionData);
    }
    
    public Task<byte[]> ReadFileDefinitionAsync(string hash)
    {
        var prefix = hash[..3];
        return _fileDefinitionFileHandlers[prefix].ReadIndexedDataAsync(Hash32.FromHexString(hash));
    }

    public async Task WriteReleasePackageAsync(byte[] releasePackageData)
    {
        var hash = ComputeHash(releasePackageData).ToHexString();
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
    
    private static Hash32 ComputeHash(byte[] data)
    {
        var hash = Hasher.Hash(data);
        return new Hash32(hash.AsSpan());
    }
    
    public StorageStatistics GetStatistics()
    {
        var stats = new StorageStatistics();
        var prefixCounts = new Dictionary<string, int>();
    
        foreach (var kvp in _chunkFileHandlers)
        {
            var handler = kvp.Value;
            var prefix = kvp.Key;
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
}