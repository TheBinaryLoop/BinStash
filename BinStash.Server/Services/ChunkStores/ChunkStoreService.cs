// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Core.Storage;

namespace BinStash.Server.Services.ChunkStores;

public sealed class ChunkStoreService : IChunkStoreService
{
    private readonly IChunkStoreStorageFactory _storageFactory;

    public ChunkStoreService(IChunkStoreStorageFactory storageFactory)
    {
        _storageFactory = storageFactory;
    }

    public async Task<(bool Success, int BytesWritten)> StoreChunkAsync(ChunkStore store, string chunkId, byte[] chunkData)
    {
        ArgumentNullException.ThrowIfNull(store);

        /*if (!store.IsWritable())
            throw new InvalidOperationException("Chunk store is read-only.");*/

        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("Chunk ID cannot be null or empty.", nameof(chunkId));

        if (chunkData == null || chunkData.Length == 0)
            throw new ArgumentException("Chunk data cannot be null or empty.", nameof(chunkData));

        var storage = _storageFactory.Create(store);
        return await storage.StoreChunkAsync(chunkId, chunkData);
    }

    public async Task<byte[]?> RetrieveChunkAsync(ChunkStore store, string chunkId)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("Chunk ID cannot be null or empty.", nameof(chunkId));

        var storage = _storageFactory.Create(store);
        return await storage.RetrieveChunkAsync(chunkId);
    }

    public Task<(bool Success, int BytesWritten)> StoreFileDefinitionAsync(ChunkStore store, Hash32 fileHash, byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be null or empty.", nameof(data));

        var storage = _storageFactory.Create(store);
        return storage.StoreFileDefinitionAsync(fileHash, data);
    }

    public Task<byte[]?> RetrieveFileDefinitionAsync(ChunkStore store, string fileHash)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
            throw new ArgumentException("File hash cannot be null or empty.", nameof(fileHash));

        var storage = _storageFactory.Create(store);
        return storage.RetrieveFileDefinitionAsync(fileHash);
    }

    public Task<bool> StoreReleasePackageAsync(ChunkStore store, byte[] packageData)
    {
        if (packageData == null || packageData.Length == 0)
            throw new ArgumentException("Package data cannot be null or empty.", nameof(packageData));

        var storage = _storageFactory.Create(store);
        return storage.StoreReleasePackageAsync(packageData);
    }

    public Task<byte[]?> RetrieveReleasePackageAsync(ChunkStore store, string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty.", nameof(packageId));

        var storage = _storageFactory.Create(store);
        return storage.RetrieveReleasePackageAsync(packageId);
    }

    public Task<bool> DeleteReleasePackageAsync(ChunkStore store, string packageId)
    {
        var storage = _storageFactory.Create(store);
        return storage.DeleteReleasePackageAsync(packageId);
    }

    public Task<bool> RebuildStorageAsync(ChunkStore store)
    {
        var storage = _storageFactory.Create(store);
        return storage.RebuildStorageAsync();
    }
}