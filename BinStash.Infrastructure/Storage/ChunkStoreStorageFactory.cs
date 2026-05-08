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

using System.Collections.Concurrent;
using BinStash.Core.Entities;
using BinStash.Core.Storage;
using BinStash.Infrastructure.Storage.S3;

namespace BinStash.Infrastructure.Storage;

public sealed class ChunkStoreStorageFactory : IChunkStoreStorageFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, IChunkStoreStorage> _cache = new();

    public IChunkStoreStorage Create(ChunkStore store)
    {
        var key = BuildCacheKey(store);
        return _cache.GetOrAdd(key, _ => store.Type switch
        {
            ChunkStoreType.Local => CreateLocalStorage(store),
            ChunkStoreType.S3 => CreateS3Storage(store),
            _ => throw new NotSupportedException($"Chunk store type '{store.Type}' is not supported.")
        });
    }

    private static LocalFolderChunkStoreStorage CreateLocalStorage(ChunkStore store)
    {
        var settings = store.GetBackendSettings<LocalFolderBackendSettings>();
        return new LocalFolderChunkStoreStorage(settings.Path);
    }

    private static S3ChunkStoreStorage CreateS3Storage(ChunkStore store)
    {
        var settings = store.GetBackendSettings<S3BackendSettings>();
        return new S3ChunkStoreStorage(settings, store.Id);
    }

    private static string BuildCacheKey(ChunkStore store) => store.Type switch
    {
        ChunkStoreType.Local => $"Local:{store.GetBackendSettings<LocalFolderBackendSettings>().Path}",
        ChunkStoreType.S3 => BuildS3CacheKey(store),
        _ => $"{store.Type}:{store.Id}"
    };

    private static string BuildS3CacheKey(ChunkStore store)
    {
        var s = store.GetBackendSettings<S3BackendSettings>();
        // Identity: same bucket + prefix + service URL uniquely identifies an S3 store.
        return $"S3:{s.BucketName}:{s.Prefix}:{s.ServiceUrl ?? string.Empty}";
    }

    public void Dispose()
    {
        foreach (var entry in _cache.Values)
        {
            if (entry is IDisposable d)
                d.Dispose();
        }

        _cache.Clear();
    }
}
