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
using BinStash.Core.Storage;

namespace BinStash.Infrastructure.Storage;

public class LocalFolderObjectStorage : IObjectStorage
{
    private readonly ObjectStore _objectStore;
    
    public LocalFolderObjectStorage(string basePath)
    {
        if (!Directory.Exists(basePath))
            Directory.CreateDirectory(basePath);
        _objectStore = ObjectStoreManager.GetOrCreateChunkStorage(basePath);
    }

    public Task<bool> RebuildStorageAsync()
    {
        return _objectStore.RebuildStorageAsync();
    }
    
    public async Task<(bool Success, int BytesWritten)> StoreChunkAsync(string key, byte[] data)
    {
        var bytesWritten = await _objectStore.WriteChunkAsync(data);
        return (true, bytesWritten);
    }

    public Task<byte[]?> RetrieveChunkAsync(string key)
    {
        return _objectStore.ReadChunkAsync(key)!;
    }

    public async Task<(bool Success, int BytesWritten)> StoreFileDefinitionAsync(Hash32 fileHash, byte[] data)
    {
        var bytesWritten = await _objectStore.WriteFileDefinitionAsync(fileHash, data);
        return (true, bytesWritten);
    }

    public Task<byte[]?> RetrieveFileDefinitionAsync(string key)
    {
        return _objectStore.ReadFileDefinitionAsync(key)!;
    }

    public async Task<bool> StoreReleasePackageAsync(byte[] packageData)
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

    public Task<Dictionary<string, object>> GetStorageStatsAsync()
    {
        return Task.FromResult(_objectStore.GetStatistics().ToDictionary());
    }
}