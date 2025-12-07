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

using BinStash.Contracts.Release;
using BinStash.Contracts.Repos;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class RepositoryEndpoints
{
    public static RouteGroupBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/repositories")
            .WithTags("Repositories")
            .RequireAuthorization();

        group.MapPost("/", CreateRepositoryAsync)
            .WithDescription("Create a new repository.")
            .WithSummary("Create Repository")
            .Produces<RepositorySummaryDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound);
        
        group.MapGet("/", ListRepositoriesAsync)
            .WithDescription("List all repositories.")
            .WithSummary("List Repositories")
            .Produces<IEnumerable<RepositorySummaryDto>>()
            .Produces(StatusCodes.Status404NotFound);
        
        group.MapGet("/{id:guid}", GetRepositoryByIdAsync)
            .WithDescription("Get a repository by ID.")
            .WithSummary("Get Repository")
            .Produces<RepositorySummaryDto>()
            .Produces(StatusCodes.Status404NotFound);
        
        group.MapGet("/{id:guid}/config", GetRepositoryConfigAsync)
            .WithDescription("Get the configuration of a repository.")
            .WithSummary("Get Repository Config")
            .Produces<RepositoryConfigDto>()
            .Produces(StatusCodes.Status404NotFound);
        
        group.MapGet("/{id:guid}/releases", GetReleasesForRepositoryAsync)
            .WithDescription("Get all releases for a repository.")
            .WithSummary("Get Repository Releases")
            .Produces<IEnumerable<ReleaseSummaryDto>>()
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }
    
    private static async Task<IResult> CreateRepositoryAsync(CreateRepositoryDto dto, BinStashDbContext db)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest("Repository name is required.");
            
        if (db.Repositories.Any(x => x.Name == dto.Name))
            return Results.Conflict($"A repository with the name '{dto.Name}' already exists.");
            
        if (dto.ChunkStoreId == Guid.Empty)
            return Results.BadRequest("Chunk store ID is required.");
            
        var chunkStore = await db.ChunkStores.FindAsync(dto.ChunkStoreId);
        if (chunkStore == null)
            return Results.NotFound($"Chunk store with ID '{dto.ChunkStoreId}' not found.");

        var repo = new Repository
        {
            Name = dto.Name,
            ChunkStore = chunkStore,
            ChunkStoreId = chunkStore.Id,
            Description = dto.Description
        };
            
        await db.Repositories.AddAsync(repo);
            
        await db.SaveChangesAsync();
            
        return Results.Created($"/api/repositories/{repo.Id}", new RepositorySummaryDto
        {
            Id = repo.Id,
            Name = chunkStore.Name,
            ChunkStoreId = chunkStore.Id,
            Description = repo.Description
        });
    }
    
    private static async Task<IResult> ListRepositoriesAsync(BinStashDbContext db)
    {
        var repos = await db.Repositories
            .Select(r => new RepositorySummaryDto
            {
                Id = r.Id,
                Name = r.Name,
                ChunkStoreId = r.ChunkStoreId,
                Description = r.Description
            })
            .ToListAsync();

        return Results.Ok(repos);
    }
    
    private static async Task<IResult> GetRepositoryByIdAsync(Guid id, BinStashDbContext db)
    {
        var repo = await db.Repositories.FindAsync(id);
        if (repo == null)
            return Results.NotFound();

        return Results.Ok(new RepositorySummaryDto
        {
            Id = repo.Id,
            Name = repo.Name,
            Description = repo.Description,
            ChunkStoreId = repo.ChunkStoreId
        });
    }
    
    private static async Task<IResult> GetRepositoryConfigAsync(Guid id, BinStashDbContext db)
    {
        var repo = await db.Repositories.AsNoTracking().Include(x => x.ChunkStore).FirstOrDefaultAsync(x => x.Id == id);
        if (repo == null)
            return Results.NotFound();

        return Results.Ok(new RepositoryConfigDto
        {
            DedupeConfig = new()
            {
                Chunker = repo.ChunkStore.ChunkerOptions.Type.ToString(),
                MinChunkSize = repo.ChunkStore.ChunkerOptions.MinChunkSize,
                AvgChunkSize = repo.ChunkStore.ChunkerOptions.AvgChunkSize,
                MaxChunkSize = repo.ChunkStore.ChunkerOptions.MaxChunkSize,
                ShiftCount = repo.ChunkStore.ChunkerOptions.ShiftCount,
                BoundaryCheckBytes = repo.ChunkStore.ChunkerOptions.BoundaryCheckBytes
            }
        });
    }

    private static async Task<IResult> GetReleasesForRepositoryAsync(Guid id, BinStashDbContext db)
    {
        var repo = await db.Repositories.AsNoTracking().Include(x => x.Releases).FirstOrDefaultAsync(x => x.Id == id);
        if (repo == null)
            return Results.NotFound();

        return Results.Ok(repo.Releases.Select(x => new ReleaseSummaryDto
        {
            Id = x.Id,
            Version = x.Version,
            CreatedAt = x.CreatedAt,
            Notes = x.Notes,
            Repository = new RepositorySummaryDto
            {
                Id = repo.Id,
                Name = repo.Name,
                Description = repo.Description,
                ChunkStoreId = repo.ChunkStoreId
            }
        }));
    }

}