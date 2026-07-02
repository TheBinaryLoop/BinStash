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

using BinStash.Contracts.ChunkStore;
using BinStash.Core.Entities;
using BinStash.Core.Storage;
using BinStash.Infrastructure.Data;
using BinStash.Server.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace BinStash.Server.Endpoints;

public static class ChunkStoreEndpoints
{
    // Chunk-store reads (list, detail, stats, enabled types), creation, and the rebuild/upgrade
    // jobs are all served via GraphQL. These handlers are reused directly (not as REST routes)
    // by the setup wizard (SetupEndpoints), which runs before GraphQL auth is available.

    internal static async Task<IResult> ListChunkStoresAsync(BinStashDbContext db)
    {
        var stores = await db.ChunkStores.Select(x => new ChunkStoreSummaryDto
        {
            Id = x.Id,
            Name = x.Name
        }).ToListAsync();
        return Results.Ok(stores);
    }

    internal static async Task<IResult> CreateChunkStoreAsync(CreateChunkStoreDto dto, BinStashDbContext db, IOptions<StorageSettings> storageOptions)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest("Chunk store name is required.");
        
        if (db.ChunkStores.Any(x => x.Name == dto.Name))
            return Results.Conflict($"A chunk store with the name '{dto.Name}' already exists.");

        // Check if the type is valid, otherwise return non-success
        var isValidChunkStoreType = Enum.TryParse<ChunkStoreType>(dto.Type, true, out var chunkStoreType);
        if (!isValidChunkStoreType)
            return Results.BadRequest($"Invalid chunk store type '{dto.Type}'.");
        
        // Build backend settings based on the type
        ChunkStoreBackendSettings backendSettings;
        switch (chunkStoreType)
        {
            case ChunkStoreType.Local:
            {
                if (string.IsNullOrWhiteSpace(dto.LocalPath))
                    return Results.BadRequest("Local path is required for local chunk store type.");

                var localPath = dto.LocalPath.Trim();

                // Reject UNC paths (\\server\share or //server/share)
                if (localPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                    localPath.StartsWith("//", StringComparison.Ordinal))
                    return Results.BadRequest("UNC paths are not permitted for local chunk store paths.");

                // Require an absolute path
                if (!Path.IsPathRooted(localPath))
                    return Results.BadRequest("LocalPath must be an absolute path.");

                // Canonicalise to eliminate traversal sequences (e.g. /../)
                string canonicalPath;
                try
                {
                    canonicalPath = Path.GetFullPath(localPath);
                }
                catch (Exception e)
                {
                    return Results.BadRequest($"LocalPath is not a valid filesystem path: {e.Message}");
                }

                // Enforce allowed-root constraint when configured
                var allowedRoot = storageOptions.Value.AllowedRootPath;
                if (!string.IsNullOrWhiteSpace(allowedRoot))
                {
                    // Canonicalise the allowed root as well so comparisons are reliable
                    string canonicalRoot;
                    try
                    {
                        canonicalRoot = Path.GetFullPath(allowedRoot);
                    }
                    catch (Exception e)
                    {
                        return Results.Problem(
                            $"Server configuration error: Storage:AllowedRootPath is invalid: {e.Message}",
                            statusCode: StatusCodes.Status500InternalServerError);
                    }

                    // Ensure the path is strictly inside the allowed root.
                    // Append separator to both sides to avoid a partial-prefix
                    // match (e.g. /data/stores vs /data/stores-extra).
                    var rootWithSep = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                      + Path.DirectorySeparatorChar;
                    var pathWithSep = canonicalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                      + Path.DirectorySeparatorChar;

                    if (!pathWithSep.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(
                            $"LocalPath must reside within the configured allowed root '{canonicalRoot}'. " +
                            "Paths outside this root are not permitted.");
                }

                if (!Directory.Exists(canonicalPath))
                {
                    try
                    {
                        Directory.CreateDirectory(canonicalPath);
                    }
                    catch (Exception e)
                    {
                        return Results.Problem($"Failed to create local path: {e.Message}", statusCode: 400);
                    }
                }

                backendSettings = new LocalFolderBackendSettings { Path = canonicalPath };
                break;
            }
            default:
                return Results.BadRequest($"Chunk store type '{dto.Type}' is not yet supported.");
        }
        
        // Validate chunker options or set defaults
        var chunkerOptions = dto.Chunker == null ? ChunkerOptions.Default(ChunkerType.FastCdc) : new ChunkerOptions
        {
            Type = Enum.TryParse<ChunkerType>(dto.Chunker.Type, true, out var chunkerType) ? chunkerType : ChunkerType.FastCdc,
            MinChunkSize = dto.Chunker.MinChunkSize ?? 2048,
            AvgChunkSize = dto.Chunker.AvgChunkSize ?? 8192,
            MaxChunkSize = dto.Chunker.MaxChunkSize ?? 65536
        };

        var chunkerErrors = chunkerOptions.Validate();
        if (chunkerErrors.Count > 0)
            return Results.BadRequest($"Invalid chunker options: {string.Join("; ", chunkerErrors)}");
        
        var chunkStore = new ChunkStore(dto.Name, chunkStoreType, backendSettings)
        {
            ChunkerOptions = chunkerOptions
        };

        db.ChunkStores.Add(chunkStore);
        await db.SaveChangesAsync();

        return Results.Ok(new ChunkStoreSummaryDto
        {
            Id = chunkStore.Id,
            Name = chunkStore.Name
        });
    }
}
