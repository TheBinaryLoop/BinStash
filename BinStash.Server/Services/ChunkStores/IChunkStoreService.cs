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

namespace BinStash.Server.Services.ChunkStores;

public interface IChunkStoreService
{
    Task<(bool Success, int BytesWritten)> StoreChunkAsync(ChunkStore store, string chunkId, byte[] chunkData);
    Task<byte[]?> RetrieveChunkAsync(ChunkStore store, string chunkId);
    Task<(bool Success, int BytesWritten)> StoreFileDefinitionAsync(ChunkStore store, Hash32 fileHash, byte[] data);
    Task<byte[]?> RetrieveFileDefinitionAsync(ChunkStore store, string fileHash);
    Task<bool> StoreReleasePackageAsync(ChunkStore store, byte[] packageData);
    Task<byte[]?> RetrieveReleasePackageAsync(ChunkStore store, string packageId);
    Task<bool> DeleteReleasePackageAsync(ChunkStore store, string packageId);
    Task<bool> RebuildStorageAsync(ChunkStore store);
}