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

using System.Collections.Concurrent;

namespace BinStash.Infrastructure.Storage;

/// <summary>
/// Provides cached access to object stores rooted at base paths.
/// </summary>
public static class ObjectStoreManager
{
    private static readonly ConcurrentDictionary<string, ObjectStore> ObjectStores = new();
    
    public static ObjectStore GetOrCreateChunkStorage(string basePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(basePath);
        return ObjectStores.GetOrAdd(basePath, path => new ObjectStore(path));
    }
}