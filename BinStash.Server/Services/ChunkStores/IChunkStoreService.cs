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
using BinStash.Core.Storage.Stats;
using BinStash.Infrastructure.Storage.Stats;

namespace BinStash.Server.Services.ChunkStores;

public interface IChunkStoreService
{
    Task<(bool Success, int BytesWritten)> StoreChunkAsync(ChunkStore store, string chunkId, ReadOnlyMemory<byte> chunkData);
    Task<byte[]?> RetrieveChunkAsync(ChunkStore store, string chunkId);
    /// <summary>
    /// Stores a serialised <c>FileDefinitionRecord</c> blob in the pack store.
    /// The index key is the <c>FileHash</c> embedded in the record (BLAKE3 of the original file bytes).
    /// </summary>
    Task<(bool Success, Hash32 FileHash, int BytesWritten)> StoreFileDefinitionAsync(ChunkStore store, ReadOnlyMemory<byte> recordBlob);

    /// <summary>
    /// Retrieves the raw <c>FileDefinitionRecord</c> blob by its file hash
    /// (<c>BLAKE3(file bytes)</c>).
    /// </summary>
    Task<byte[]?> RetrieveFileDefinitionAsync(ChunkStore store, string fileHashHex);
    Task<bool> StoreReleasePackageAsync(ChunkStore store, ReadOnlyMemory<byte> packageData);
    Task<byte[]?> RetrieveReleasePackageAsync(ChunkStore store, string packageId);
    Task<bool> DeleteReleasePackageAsync(ChunkStore store, string packageId);
    Task<bool> RebuildStorageAsync(ChunkStore store);

    /// <summary>
    /// Rebuilds the storage index for <paramref name="store"/>, reporting incremental bucket progress.
    /// </summary>
    Task<bool> RebuildStorageWithProgressAsync(ChunkStore store, IProgress<bool> progress, CancellationToken cancellationToken);

    Task<Dictionary<string, byte[]>> RetrieveFileDefinitionsAsync(ChunkStore store, IReadOnlyCollection<string> fileHashes);
    Task<Dictionary<string, byte[]>> RetrieveReleasePackagesAsync(ChunkStore store, IReadOnlyCollection<string> packageIds);
    Task<ChunkStorePhysicalStats> GetPhysicalStatsAsync(ChunkStore store);
}