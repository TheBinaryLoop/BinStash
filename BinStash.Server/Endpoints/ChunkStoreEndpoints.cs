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

using System.Text.Json;
using System.Threading.Channels;
using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Core.Storage;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using BinStash.Server.Services.ChunkStores;
using Microsoft.EntityFrameworkCore;
using Path = System.IO.Path;

namespace BinStash.Server.Endpoints;

public static class ChunkStoreEndpoints
{
    public static RouteGroupBuilder MapChunkStoreEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Add ProducesError
        
        var group = app.MapGroup("/api/chunk-stores")!
            .WithTags("ChunkStore")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireInstancePermission(InstancePermission.Admin);
            //.WithDescription("Endpoints for managing chunk stores. Chunk stores are used to store chunks of data that are referenced by repositories. They can be local or remote, and support various chunking algorithms.");

        group.MapGet("/enabled-types", GetChunkStoreTypes)!
            .WithDescription("Get available chunk store types.")
            .WithSummary("Get Chunk Store Types")
            .Produces(StatusCodes.Status200OK);
        group.MapPost("/", CreateChunkStoreAsync)!
            .WithDescription("Creates a new chunk store.")
            .WithSummary("Create Chunk Store")
            .Produces<ChunkStoreSummaryDto>(StatusCodes.Status201Created);
        group.MapGet("/", ListChunkStoresAsync)!
            .WithDescription("Lists all chunk stores.")
            .WithSummary("List Chunk Stores")
            .Produces<List<ChunkStoreSummaryDto>>();
        group.MapGet("/{id:guid}", GetChunkStoreByIdAsync)!
            .WithDescription("Gets a chunk store by its ID.")
            .WithSummary("Get Chunk Store By ID")
            .Produces<ChunkStoreDetailDto>()
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/stats", GetChunkStoreStatsAsync)!
            .WithDescription("Gets statistics about a chunk store, such as total size, number of chunks, etc.")
            .WithSummary("Get Chunk Store Stats")
            .Produces<ChunkStoreStatsDto>()
            .Produces(StatusCodes.Status404NotFound);
        
        group.MapPost("/{id:guid}/rebuild", RebuildChunkStoreAsync)!
            .WithDescription(
                "Rebuilds the chunk store by scanning the underlying storage and rewriting the pack and index files.")
            .WithSummary("Rebuild Chunk Store")
            .Produces(StatusCodes.Status200OK);
        group.MapPost("/{id:guid}/upgrade", StartUpgradeReleasesAsync)!
            .WithDescription("Starts an asynchronous background job to upgrade all releases in the chunk store to the latest serializer version. Returns 202 Accepted with the job ID. Subscribe via GraphQL subscription 'backgroundJobProgress(jobId)' for real-time progress, or poll GET /api/upgrade-jobs/{jobId}.")
            .WithSummary("Start Release Upgrade Job")
            .Produces<UpgradeJobDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);
        /*group.MapDelete("/{id:guid}", DeleteChunkStoreAsync)
            .WithDescription("Deletes a chunk store by its ID.")
            .WithSummary("Delete Chunk Store")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);*/

        return group;
    }

    private static IResult GetChunkStoreTypes()
    {
        var types = Enum.GetValues<ChunkStoreType>()
            .Select(x => new { name = x.ToString(), value = (int)x })
            .ToList();
        return Results.Ok(types);
    }
    
    internal static async Task<IResult> CreateChunkStoreAsync(CreateChunkStoreDto dto, BinStashDbContext db)
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
                if (!Directory.Exists(dto.LocalPath))
                {
                    try
                    {
                        Directory.CreateDirectory(dto.LocalPath);
                    }
                    catch (Exception e)
                    {
                        return Results.Problem($"Failed to create local path: {e.Message}", statusCode: 400);
                    }
                }

                backendSettings = new LocalFolderBackendSettings { Path = dto.LocalPath };
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

        return Results.Created($"/api/chunk-stores/{chunkStore.Id}", new ChunkStoreSummaryDto
        {
            Id = chunkStore.Id,
            Name = chunkStore.Name
        });
    }
    
    internal static async Task<IResult> ListChunkStoresAsync(BinStashDbContext db)
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
            BackendSettings = MapBackendSettingsToDto(store),
            Stats = new Dictionary<string, object>()
        });
    }

    private static async Task<IResult> GetChunkStoreStatsAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();

        return Results.Ok(new ChunkStoreStatsDto
        {
            TotalChunks = await db.Chunks.Where(x => x.ChunkStoreId == id).CountAsync(),
        });
    }
    
    private static async Task<IResult> RebuildChunkStoreAsync(Guid id, BinStashDbContext db, IChunkStoreService chunkStoreService)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();

        var result = await chunkStoreService.RebuildStorageAsync(store);
        if (!result)
            return Results.Problem("Failed to rebuild chunk store.");
        
        return Results.Ok();
        
    }
    
    private static async Task<IResult> StartUpgradeReleasesAsync(
        Guid id,
        BinStashDbContext db,
        Channel<Guid> jobChannel)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();

        if (store.Type != ChunkStoreType.Local)
            return Results.BadRequest("Release upgrade is currently only supported for local chunk stores.");

        // Prevent duplicate running/pending jobs for the same chunk store
        var hasActiveJob = await db.BackgroundJobs.AnyAsync(j =>
            j.JobType == BackgroundJobTypes.ReleaseUpgrade
            && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running)
            && j.JobData != null && j.JobData.Contains(id.ToString()));

        if (hasActiveJob)
            return Results.Conflict("An upgrade job is already running or pending for this chunk store.");

        var jobData = new ReleaseUpgradeJobData
        {
            ChunkStoreId = id,
            TargetSerializerVersion = ReleasePackageSerializer.Version
        };

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            JobType = BackgroundJobTypes.ReleaseUpgrade,
            Status = BackgroundJobStatus.Pending,
            JobData = JsonSerializer.Serialize(jobData),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();

        // Enqueue the job for the background service
        await jobChannel.Writer.WriteAsync(job.Id);

        return Results.Accepted($"/api/upgrade-jobs/{job.Id}", UpgradeJobEndpoints.MapToDto(job));
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

    /// <summary>
    /// Maps the entity's <see cref="ChunkStoreBackendSettings"/> to the API DTO.
    /// </summary>
    private static ChunkStoreBackendSettingsDto MapBackendSettingsToDto(ChunkStore store) => store.BackendSettings switch
    {
        LocalFolderBackendSettings local => new ChunkStoreBackendSettingsDto
        {
            Type = store.Type.ToString(),
            LocalPath = local.Path
        },
        _ => new ChunkStoreBackendSettingsDto { Type = store.Type.ToString() }
    };
}
