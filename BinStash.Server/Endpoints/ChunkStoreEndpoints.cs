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
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class ChunkStoreEndpoints
{
    public static RouteGroupBuilder MapChunkStoreEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Add ProducesError
        
        var group = app.MapGroup("/api/chunkstores")
            .WithTags("ChunkStore")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();
            //.WithDescription("Endpoints for managing chunk stores. Chunk stores are used to store chunks of data that are referenced by repositories. They can be local or remote, and support various chunking algorithms.");

        group.MapPost("/", CreateChunkStoreAsync)
            .WithDescription("Creates a new chunk store.")
            .WithSummary("Create Chunk Store")
            .Produces<ChunkStoreSummaryDto>(StatusCodes.Status201Created);
        group.MapGet("/", ListChunkStoresAsync)
            .WithDescription("Lists all chunk stores.")
            .WithSummary("List Chunk Stores")
            .Produces<List<ChunkStoreSummaryDto>>()
            .RequireTenantPermission(TenantPermission.Admin);
        group.MapGet("/{id:guid}", GetChunkStoreByIdAsync)
            .WithDescription("Gets a chunk store by its ID.")
            .WithSummary("Get Chunk Store By ID")
            .Produces<ChunkStoreDetailDto>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/rebuild", RebuildChunkStoreAsync)
            .WithDescription(
                "Rebuilds the chunk store by scanning the underlying storage and rewriting the pack and index files.")
            .WithSummary("Rebuild Chunk Store")
            .Produces(StatusCodes.Status200OK);
        group.MapGet("/{id:guid}/upgrade", UpgradeReleasesToLatestVersionAsync)
            .WithDescription("Upgrades all releases in the chunk store to the latest version.")
            .WithSummary("Upgrade Releases")
            .Produces(StatusCodes.Status200OK);
        /*group.MapDelete("/{id:guid}", DeleteChunkStoreAsync)
            .WithDescription("Deletes a chunk store by its ID.")
            .WithSummary("Delete Chunk Store")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);*/

        return group;
    }

    private static async Task<IResult> CreateChunkStoreAsync(CreateChunkStoreDto dto, BinStashDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest("Chunk store name is required.");
        
        if (db.ChunkStores.Any(x => x.Name == dto.Name))
            return Results.Conflict($"A chunk store with the name '{dto.Name}' already exists.");

        // Check if the type is valid, otherwise return non-success
        var isValidChunkStoreType = Enum.TryParse<ChunkStoreType>(dto.Type, true, out var chunkStoreType);
        if (!isValidChunkStoreType)
            return Results.BadRequest($"Invalid chunk store type '{dto.Type}'.");
        
        // Check if the local path is valid for local chunk store type
        if (chunkStoreType == ChunkStoreType.Local)
        {
            if (string.IsNullOrWhiteSpace(dto.LocalPath))
                return Results.BadRequest("Local path is required for local chunk store type.");
        }
        
        // Validate chunker options or set defaults
        var chunkerOptions = dto.Chunker == null ? ChunkerOptions.Default(ChunkerType.FastCdc) : new ChunkerOptions
        {
            Type = Enum.TryParse<ChunkerType>(dto.Chunker.Type, true, out var chunkerType) ? chunkerType : ChunkerType.FastCdc,
            MinChunkSize = dto.Chunker.MinChunkSize ?? 2048,
            AvgChunkSize = dto.Chunker.AvgChunkSize ?? 8192,
            MaxChunkSize = dto.Chunker.MaxChunkSize ?? 65536
        };
        
        if (chunkerOptions.MinChunkSize <= 0 || chunkerOptions.AvgChunkSize <= 0 || chunkerOptions.MaxChunkSize <= 0)
            return Results.BadRequest("Chunk sizes must be greater than zero.");
        
        var chunkStore = new ChunkStore(dto.Name, chunkStoreType, dto.LocalPath, new LocalFolderObjectStorage(dto.LocalPath))
        {
            ChunkerOptions = chunkerOptions
        };

        db.ChunkStores.Add(chunkStore);
        await db.SaveChangesAsync();

        return Results.Created($"/api/chunkstores/{chunkStore.Id}", new ChunkStoreSummaryDto
        {
            Id = chunkStore.Id,
            Name = chunkStore.Name
        });
    }
    
    private static async Task<IResult> ListChunkStoresAsync(BinStashDbContext db)
    {
        var stores = await db.ChunkStores.Select(x => new ChunkStoreSummaryDto
        {
            Id = x.Id,
            Name = x.Name
        }).ToListAsync();
        return Results.Ok(stores);
    }
    
    private static async Task<IResult> GetChunkStoreByIdAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();

        return Results.Ok(new ChunkStoreDetailDto
        {
            Id = store.Id,
            Name = store.Name,
            Type = store.Type.ToString(),
            Chunker = new ChunkStoreChunkerDto
            {
                Type = store.ChunkerOptions.Type.ToString(),
                MinChunkSize = store.ChunkerOptions.MinChunkSize,
                AvgChunkSize = store.ChunkerOptions.AvgChunkSize,
                MaxChunkSize = store.ChunkerOptions.MaxChunkSize,
            },
            Stats = new Dictionary<string, object>() // await new LocalFolderChunkStorage(store.LocalPath).GetStorageStatsAsync()
        });
    }

    private static async Task<IResult> RebuildChunkStoreAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
        
        store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));

        var result = await store.RebuildStorageAsync();
        if (!result)
            return Results.Problem("Failed to rebuild chunk store.");
        
        return Results.Ok();
        
    }
    
    private static async Task<IResult> UpgradeReleasesToLatestVersionAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
        
        var repos = await db.Repositories.Where(x => x.ChunkStoreId == id).ToListAsync();
        if (!repos.Any())
            return Results.Ok();
        
        var repoIds = repos.Select(x => x.Id).ToList();
        
        var releasesToUpgrade = await db.Releases.Where(r => repoIds.Contains(r.RepoId) && r.SerializerVersion < ReleasePackageSerializer.Version).ToListAsync();
        if (!releasesToUpgrade.Any())
            return Results.Ok();
        
        store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
        
        foreach (var releaseFile in Directory.EnumerateFiles(Path.Combine(store.LocalPath, "Releases"), "*.rdef",
                     SearchOption.AllDirectories))
        {
            var tmpHash = Hash32.FromHexString(Path.GetFileNameWithoutExtension(releaseFile));
            var tmpData = await File.ReadAllBytesAsync(releaseFile);
            var tmpReleasePack = await ReleasePackageSerializer.DeserializeAsync(tmpData);
            var tmpReleaseId = Guid.Parse(tmpReleasePack.ReleaseId);
            var dbRelease = releasesToUpgrade.FirstOrDefault(r => r.Id == tmpReleaseId);
            if (dbRelease == null)
            {
                File.Delete(releaseFile);
                continue;
            }
            if (dbRelease.ReleaseDefinitionChecksum == tmpHash) continue;
            dbRelease.ReleaseDefinitionChecksum = tmpHash;
        }

        var failedReleases = new Dictionary<Release, string>();
        var grownReleases = new Dictionary<Release, int>(); // Release, grown size in bytes
        
        foreach (var release in releasesToUpgrade)
        {
            var releaseData = await store.RetrieveReleasePackageAsync(release.ReleaseDefinitionChecksum.ToHexString());
            if (releaseData == null)
            {
                failedReleases[release] = "Failed to retrieve release package.";
                continue;
            }

            ReleasePackage releasePackage;
            try
            {
                releasePackage = await ReleasePackageSerializer.DeserializeAsync(releaseData);
            }
            catch (Exception e)
            {
                failedReleases[release] = $"Failed to deserialize release package: {e.Message}";
                continue;
            }
            
            var oldReleaseDefinitionChecksum = release.ReleaseDefinitionChecksum;

            var serializedReleasePackage = await ReleasePackageSerializer.SerializeAsync(releasePackage);
            
            if (serializedReleasePackage.Length > releaseData.Length)
                grownReleases[release] = serializedReleasePackage.Length - releaseData.Length;
            
            var hash = new Hash32(Blake3.Hasher.Hash(serializedReleasePackage).AsSpan());
            await store.StoreReleasePackageAsync(serializedReleasePackage);
            release.SerializerVersion = ReleasePackageSerializer.Version;
            release.ReleaseDefinitionChecksum = hash;
            await db.SaveChangesAsync();
            try
            {
               await store.DeleteReleasePackageAsync(oldReleaseDefinitionChecksum.ToHexString());
            }
            catch (Exception e)
            {
                failedReleases[release] = $"Failed to delete old release package: {e.Message}";
            }
        }
        return Results.Json(new { failedReleases = failedReleases.Select(x => new {x.Key.Id, x.Value}).ToList(), grownReleases = grownReleases.Select(x => new {x.Key.Id, x.Value}).ToList(), totalSizeGrowth = grownReleases.Sum(x => x.Value), totalReleases = releasesToUpgrade.Count, successfulUpgrades = releasesToUpgrade.Count - failedReleases.Count });
    }

    private static async Task<IResult> DeleteChunkStoreAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
            
        // TODO: Check if the store is in use by any repository before deleting
        // TODO: Delete the physical store if it's a local store

        return Results.Conflict();
    }

}