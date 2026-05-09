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

namespace BinStash.Core.Storage;

public interface IChunkStoreStorage
{
    Task<(bool Success, int BytesWritten)> StoreChunkAsync(string key, ReadOnlyMemory<byte> data);
    Task<byte[]?> RetrieveChunkAsync(string key);

    /// <summary>
    /// Stores a serialised <c>FileDefinitionRecord</c> blob in the pack store.
    /// The index key is the <c>FileHash</c> embedded in the record (BLAKE3 of the original file bytes).
    /// </summary>
    /// <returns>
    /// Success flag, file hash (<c>BLAKE3(file bytes)</c>), and the number of
    /// compressed bytes physically written (0 = already existed / deduplicated).
    /// </returns>
    Task<(bool Success, Hash32 FileHash, int BytesWritten)> StoreFileDefinitionAsync(ReadOnlyMemory<byte> recordBlob);

    /// <summary>
    /// Retrieves the raw <c>FileDefinitionRecord</c> blob by its file hash
    /// (<c>BLAKE3(file bytes)</c>).
    /// </summary>
    Task<byte[]?> RetrieveFileDefinitionAsync(string fileHashHex);

    Task<bool> StoreReleasePackageAsync(ReadOnlyMemory<byte> packageData);
    Task<byte[]?> RetrieveReleasePackageAsync(string key);
    Task<bool> DeleteReleasePackageAsync(string packageId);

    /// <summary>
    /// Retrieves multiple file definition blobs in parallel, keyed by
    /// their file-hash hex strings.
    /// </summary>
    Task<Dictionary<string, byte[]>> RetrieveFileDefinitionsAsync(IReadOnlyCollection<string> fileHashHexes);
    Task<Dictionary<string, byte[]>> RetrieveReleasePackagesAsync(IReadOnlyCollection<string> packageIds);
    Task<ChunkStorePhysicalStats> GetPhysicalStatsAsync();

    Task<bool> RebuildStorageAsync();

    /// <summary>
    /// Rebuilds the storage index, reporting incremental progress via <paramref name="progress"/>.
    /// The progress callback receives <c>true</c> when a bucket succeeded, <c>false</c> when it failed.
    /// </summary>
    Task<bool> RebuildStorageWithProgressAsync(IProgress<bool> progress, CancellationToken cancellationToken);

    Task<Dictionary<string, object>> GetStorageStatsAsync();
}