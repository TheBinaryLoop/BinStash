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

namespace BinStash.Core.Storage;

public interface IObjectStorage
{
    Task<(bool Success, int BytesWritten)> StoreChunkAsync(string key, byte[] data);
    Task<byte[]?> RetrieveChunkAsync(string key);
    
    Task<(bool Success, int BytesWritten)> StoreFileDefinitionAsync(Hash32 fileHash, byte[] data);
    Task<byte[]?> RetrieveFileDefinitionAsync(string key);
    
    Task<bool> StoreReleasePackageAsync(byte[] packageData);
    Task<byte[]?> RetrieveReleasePackageAsync(string key);

    Task<bool> RebuildStorageAsync();
    Task<Dictionary<string, object>> GetStorageStatsAsync();
}