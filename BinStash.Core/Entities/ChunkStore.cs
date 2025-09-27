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

using BinStash.Core.Storage;

namespace BinStash.Core.Entities;

public class ChunkStore
{
    public Guid Id { get; }
    public string Name { get; private set; }

    // Chunker settings
    public ChunkerOptions ChunkerOptions { get; init; } = ChunkerOptions.Default(ChunkerType.FastCdc);
    

    // TODO: Make the options somehow linked to the type
    public ChunkStoreType Type { get; private set; }
    
    // Settings for the local chunk store type
    public string LocalPath { get; private set; }
    
    private readonly IObjectStorage? _storage;
    
    
    public ChunkStore(string name, ChunkStoreType type, string localPath)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        
        Type = type;
        LocalPath = localPath;
        if (!Directory.Exists(localPath))
            Directory.CreateDirectory(localPath);
    }
    
    public ChunkStore(string name, ChunkStoreType type, string localPath, IObjectStorage storage)
        : this(name, type, localPath)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage), "Storage cannot be null.");
    }
    
    public async Task<(bool Success, int BytesWritten)> StoreChunkAsync(string chunkId, byte[] chunkData)
    {
        if (_storage == null)
            throw new InvalidOperationException("Chunk storage is not initialized.");
        
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("Chunk ID cannot be null or empty.", nameof(chunkId));
        
        if (chunkData == null || chunkData.Length == 0)
            throw new ArgumentException("Chunk data cannot be null or empty.", nameof(chunkData));
        
        return await _storage.StoreChunkAsync(chunkId, chunkData);
    }

    public async Task<byte[]?> RetrieveChunkAsync(string chunkId)
    {
        if (_storage == null)
            throw new InvalidOperationException("Chunk storage is not initialized.");
        
        if (string.IsNullOrWhiteSpace(chunkId))
            throw new ArgumentException("Chunk ID cannot be null or empty.", nameof(chunkId));
        
        return await _storage.RetrieveChunkAsync(chunkId);
    }

    public async Task<bool> StoreReleasePackageAsync(byte[] packageData)
    {
        if (_storage == null)
            throw new InvalidOperationException("Chunk storage is not initialized.");
        
        if (packageData == null || packageData.Length == 0)
            throw new ArgumentException("Package data cannot be null or empty.", nameof(packageData));
        
        return await _storage.StoreReleasePackageAsync(packageData);
    }

    public async Task<byte[]?> RetrieveReleasePackageAsync(string packageId)
    {
        if (_storage == null)
            throw new InvalidOperationException("Chunk storage is not initialized.");

        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty.", nameof(packageId));

        return await _storage.RetrieveReleasePackageAsync(packageId);
    }
}

public enum ChunkStoreType
{
    Local,
    S3
}