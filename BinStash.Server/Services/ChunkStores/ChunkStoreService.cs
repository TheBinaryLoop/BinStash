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
using BinStash.Core.Storage.Stats;
using BinStash.Infrastructure.Storage.Stats;

namespace BinStash.Server.Services.ChunkStores;

public sealed class ChunkStoreService : IChunkStoreService
{
    private readonly IChunkStoreStorageFactory _storageFactory;

    public ChunkStoreService(IChunkStoreStorageFactory storageFactory)
    {
        _storageFactory = storageFactory;
    }

    public async Task<(bool Success, int BytesWritten)> StoreChunkAsync(ChunkStore store, string chunkId, ReadOnlyMemory<byte> chunkData)
    {
        ArgumentNullException.ThrowIfNull(store);

        /*if (!store.IsWritable())
            throw new InvalidOperationException("Chunk store is read-only.");*/

        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("Chunk ID cannot be null or empty.", nameof(chunkId));

        if (chunkData.IsEmpty)
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

    public Task<(bool Success, Hash32 FileHash, int BytesWritten)> StoreFileDefinitionAsync(ChunkStore store, ReadOnlyMemory<byte> recordBlob)
    {
        if (recordBlob.IsEmpty)
            throw new ArgumentException("Record blob cannot be empty.", nameof(recordBlob));

        var storage = _storageFactory.Create(store);
        return storage.StoreFileDefinitionAsync(recordBlob);
    }

    public Task<byte[]?> RetrieveFileDefinitionAsync(ChunkStore store, string fileHashHex)
    {
        if (string.IsNullOrWhiteSpace(fileHashHex))
            throw new ArgumentException("File hash cannot be null or empty.", nameof(fileHashHex));

        var storage = _storageFactory.Create(store);
        return storage.RetrieveFileDefinitionAsync(fileHashHex);
    }

    public Task<bool> StoreReleasePackageAsync(ChunkStore store, ReadOnlyMemory<byte> packageData)
    {
        if (packageData.IsEmpty)
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

    public Task<bool> RebuildStorageWithProgressAsync(ChunkStore store, IProgress<bool> progress, CancellationToken cancellationToken)
    {
        var storage = _storageFactory.Create(store);
        return storage.RebuildStorageWithProgressAsync(progress, cancellationToken);
    }

    public Task<Dictionary<string, byte[]>> RetrieveFileDefinitionsAsync(ChunkStore store, IReadOnlyCollection<string> fileHashes)
    {
        var storage = _storageFactory.Create(store);
        return storage.RetrieveFileDefinitionsAsync(fileHashes);
    }

    public Task<Dictionary<string, byte[]>> RetrieveReleasePackagesAsync(ChunkStore store, IReadOnlyCollection<string> packageIds)
    {
        var storage = _storageFactory.Create(store);
        return storage.RetrieveReleasePackagesAsync(packageIds);
    }

    public Task<ChunkStorePhysicalStats> GetPhysicalStatsAsync(ChunkStore store)
    {
        var storage = _storageFactory.Create(store);
        return storage.GetPhysicalStatsAsync();
    }
}