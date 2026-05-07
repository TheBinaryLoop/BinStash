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

using System.Collections.Concurrent;
using BinStash.Contracts.Hashing;
using BinStash.Core.Storage;
using BinStash.Core.Storage.Stats;

namespace BinStash.Infrastructure.Storage;

public class LocalFolderChunkStoreStorage : IChunkStoreStorage, IDisposable, IAsyncDisposable
{
    private readonly ObjectStore _objectStore;
    
    public LocalFolderChunkStoreStorage(string basePath)
    {
        Directory.CreateDirectory(basePath);
        _objectStore = ObjectStoreManager.GetOrCreateChunkStorage(basePath);
    }

    public Task<ChunkStorePhysicalStats> GetPhysicalStatsAsync()
    {
        return _objectStore.GetPhysicalStatsAsync();
    }

    public Task<bool> RebuildStorageAsync()
    {
        return _objectStore.RebuildStorageAsync();
    }
    
    public async Task<(bool Success, int BytesWritten)> StoreChunkAsync(string key, ReadOnlyMemory<byte> data)
    {
        var bytesWritten = await _objectStore.WriteChunkAsync(data);
        return (true, bytesWritten);
    }

    public Task<byte[]?> RetrieveChunkAsync(string key)
    {
        return _objectStore.ReadChunkAsync(key)!;
    }

    public async Task<(bool Success, int BytesWritten)> StoreFileDefinitionAsync(Hash32 fileHash, ReadOnlyMemory<byte> data)
    {
        var bytesWritten = await _objectStore.WriteFileDefinitionAsync(fileHash, data);
        return (true, bytesWritten);
    }

    public Task<byte[]?> RetrieveFileDefinitionAsync(string key)
    {
        return _objectStore.ReadFileDefinitionAsync(key)!;
    }

    public async Task<bool> StoreReleasePackageAsync(ReadOnlyMemory<byte> packageData)
    {
        await _objectStore.WriteReleasePackageAsync(packageData);
        return true;
    }

    public async Task<byte[]?> RetrieveReleasePackageAsync(string key)
    {
        return await _objectStore.ReadReleasePackageAsync(key);
    }
    
    public async Task<bool> DeleteReleasePackageAsync(string packageId)
    {
        return await _objectStore.DeleteReleasePackageAsync(packageId);
    }

    public async Task<Dictionary<string, byte[]>> RetrieveFileDefinitionsAsync(IReadOnlyCollection<string> fileHashes)
    {
        var result = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var throttler = new SemaphoreSlim(16);

        await Task.WhenAll(fileHashes.Select(async hash =>
        {
            await throttler.WaitAsync();
            try
            {
                var data = await RetrieveFileDefinitionAsync(hash);
                if (data != null)
                    result[hash] = data;
            }
            finally
            {
                throttler.Release();
            }
        }));

        return new Dictionary<string, byte[]>(result, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, byte[]>> RetrieveReleasePackagesAsync(IReadOnlyCollection<string> packageIds)
    {
        var result = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var throttler = new SemaphoreSlim(16);

        await Task.WhenAll(packageIds.Select(async hash =>
        {
            await throttler.WaitAsync();
            try
            {
                var data = await RetrieveReleasePackageAsync(hash);
                if (data != null)
                    result[hash] = data;
            }
            finally
            {
                throttler.Release();
            }
        }));

        return new Dictionary<string, byte[]>(result, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, object>> GetStorageStatsAsync()
    {
        return (await _objectStore.GetStatisticsAsync()).ToDictionary();
    }
    
    public void Dispose() => _objectStore.Dispose();
    public ValueTask DisposeAsync() => _objectStore.DisposeAsync();
}