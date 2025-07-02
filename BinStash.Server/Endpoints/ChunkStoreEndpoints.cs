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

using System.Security.Cryptography;
using BinStash.Contracts.ChunkStore;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class ChunkStoreEndpoints
{
    public static RouteGroupBuilder MapChunkStoreEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: Add ProducesError
        
        var group = app.MapGroup("/api/chunkstores")
            .WithTags("ChunkStore");
            //.WithDescription("Endpoints for managing chunk stores. Chunk stores are used to store chunks of data that are referenced by repositories. They can be local or remote, and support various chunking algorithms.");

        group.MapPost("/", CreateChunkStoreAsync)
            .WithDescription("Creates a new chunk store.")
            .WithSummary("Create Chunk Store")
            .Produces<ChunkStoreSummaryDto>(StatusCodes.Status201Created);
        group.MapGet("/", ListChunkStoresAsync)
            .WithDescription("Lists all chunk stores.")
            .WithSummary("List Chunk Stores")
            .Produces<List<ChunkStoreSummaryDto>>();
        group.MapGet("/{id:guid}", GetChunkStoreByIdAsync)
            .WithDescription("Gets a chunk store by its ID.")
            .WithSummary("Get Chunk Store By ID")
            .Produces<ChunkStoreDetailDto>()
            .Produces(StatusCodes.Status404NotFound);
        /*group.MapDelete("/{id:guid}", DeleteChunkStoreAsync)
            .WithDescription("Deletes a chunk store by its ID.")
            .WithSummary("Delete Chunk Store")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);*/
        group.MapPost("/{id:guid}/chunks/missing", GetMissingChunksAsync)
            .WithDescription("Gets a list of missing chunks in the chunk store.")
            .WithSummary("Get Missing Chunks")
            .Produces<ChunkStoreMissingChunkSyncInfoDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        group.MapPost("/{id:guid}/chunks/{chunkChecksum:length(64)}", UploadChunkAsync)
            .WithDescription("Uploads a single chunk to the chunk store.")
            .WithSummary("Upload Chunk")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);
        group.MapPost("/{id:guid}/chunks/batch", UploadChunksBatchAsync)
            .WithDescription("Uploads a batch of chunks to the chunk store.")
            .WithSummary("Upload Chunks Batch")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

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
    
    private static async Task<IResult> DeleteChunkStoreAsync(Guid id, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
            
        // TODO: Check if the store is in use by any repository before deleting
        // TODO: Delete the physical store if it's a local store

        return Results.Conflict();
    }

    private static async Task<IResult> GetMissingChunksAsync(Guid id, ChunkStoreMissingChunkSyncInfoDto dto, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
            
        // Return a list of all chunks that are in the dto but not in the database with the store id
        if (!dto.ChunkChecksums.Any())
            return Results.BadRequest("No chunk checksums provided.");
            
        var knownChecksums = await db.Chunks
            .Where(c => c.ChunkStoreId == id && dto.ChunkChecksums.Contains(c.Checksum))
            .Select(c => c.Checksum)
            .ToListAsync();
            
        var missingChecksums = dto.ChunkChecksums.Except(knownChecksums).ToList();
            
        if (!missingChecksums.Any())
            return Results.Ok(new ChunkStoreMissingChunkSyncInfoDto
            {
                ChunkChecksums = []
            });
            
        return Results.Ok(new ChunkStoreMissingChunkSyncInfoDto
        {
            ChunkChecksums = missingChecksums
        });
    }

    private static async Task<IResult> UploadChunkAsync(Guid id, string chunkChecksum, BinStashDbContext db, Stream chunkStream)
    {
        var store = await db.ChunkStores.FindAsync(id);
        if (store == null)
            return Results.NotFound();
            
        store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));
            
        using var ms = new MemoryStream();
        await chunkStream.CopyToAsync(ms);
        ms.Position = 0;

        if (db.Chunks.Any(c => c.Checksum == chunkChecksum && c.ChunkStoreId == id)) return Results.Ok();
        if (!await store.StoreChunkAsync(chunkChecksum, ms.ToArray())) return Results.Problem();
        db.Chunks.Add(new Chunk
        {
            Checksum = chunkChecksum,
            ChunkStoreId = id,
            Length = ms.Length
        });
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static async Task<IResult> UploadChunksBatchAsync(Guid id, List<ChunkUploadDto> chunks, BinStashDbContext db)
    {
        var store = await db.ChunkStores.FindAsync(id);
            if (store == null)
                return Results.NotFound();

            store = new ChunkStore(store.Name, store.Type, store.LocalPath, new LocalFolderObjectStorage(store.LocalPath));

            if (!chunks.Any())
                return Results.BadRequest("No chunks provided.");

            // Deduplicate the chunks based on checksum
            var uniqueChunks = chunks
                .GroupBy(c => c.Checksum)
                .Select(g => g.First()) // Take the first occurrence of each checksum
                .ToList();
            

            var checksums = uniqueChunks.Select(c => c.Checksum).ToList();

            var knownChecksums = await db.Chunks
                .Where(c => c.ChunkStoreId == id && checksums.Contains(c.Checksum))
                .Select(c => c.Checksum)
                .ToListAsync();

            var missingChecksums = checksums.Except(knownChecksums).ToList();

            var tasks = new List<Task<bool>>();
            await Parallel.ForEachAsync(uniqueChunks.Where(x => missingChecksums.Contains(x.Checksum)), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (chunk, _) => {
                var hash = Convert.ToHexString(SHA256.HashData(chunk.Data));
                if (!hash.Equals(chunk.Checksum, StringComparison.OrdinalIgnoreCase))
                    return; // TODO: Handle checksum mismatch

                // Store to filesystem asynchronously
                if (!await store.StoreChunkAsync(chunk.Checksum, chunk.Data))
                    throw new Exception($"Failed to store chunk {chunk.Checksum} in store {store.Name}");
            });

            // Add unique chunks to DbContext ensuring no duplicates
            var chunksToAdd = uniqueChunks
                .Where(x => missingChecksums.Contains(x.Checksum))
                .Select(chunk => new Chunk
                {
                    Checksum = chunk.Checksum,
                    ChunkStoreId = id,
                    Length = chunk.Data.Length
                });

            db.Chunks.AddRange(chunksToAdd);
            await db.SaveChangesAsync();

            return Results.Ok();
    }
}