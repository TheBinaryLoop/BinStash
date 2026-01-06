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

using BinStash.Contracts.ChunkStore;
using BinStash.Contracts.Release;
using BinStash.Contracts.Repo;
using BinStash.Core.Auth;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Context;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class RepositoryEndpoints
{
    public static RouteGroupBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId:guid}/repositories")
            .WithTags("Repositories")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();
        
        var tenantFromHostGroup = app.MapGroup("/api/repositories")
            .WithTags("Repositories")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();
        
        MapGroup(group);
        MapGroup(tenantFromHostGroup);

        return group;
    }

    private static void MapGroup(RouteGroupBuilder group)
    {
        group.MapPost("/", CreateRepositoryAsync)
            .WithDescription("Create a new repository.")
            .WithSummary("Create Repository")
            .Produces<RepositorySummaryDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .RequireTenantPermission(TenantPermission.Admin);
        
        group.MapGet("/", ListRepositoriesAsync)
            .WithDescription("List all repositories.")
            .WithSummary("List Repositories")
            .Produces<IEnumerable<RepositorySummaryDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireTenantPermission(TenantPermission.Member);
        
        group.MapGet("/{repoId:guid}", GetRepositoryByIdAsync)
            .WithDescription("Get a repository by ID.")
            .WithSummary("Get Repository")
            .Produces<RepositorySummaryDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireRepoPermission(RepositoryPermission.Read);
        
        group.MapGet("/{repoId:guid}/access", GetRepositoryMemberAccessInfosAsync)
            .WithDescription("Get access control information for a repository.")
            .WithSummary("Get Repository Access Info")
            .Produces<List<RepositoryAccessDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireRepoPermission(RepositoryPermission.Admin);
        
        group.MapPost("/{repoId:guid}/access", SetRepositoryMemberAccessInfosAsync)
            .WithDescription("Set access control information for a repository.")
            .WithSummary("Set Repository Access Info")
            .Produces<RepositoryAccessDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireRepoPermission(RepositoryPermission.Admin);
        
        group.MapDelete("/{repoId:guid}/access/{subjectType}/{subjectId}", DeleteRepositoryMemberAccessAsync)
            .WithDescription("Delete access control information for a repository.")
            .WithSummary("Delete Repository Access Info")
            .RequireRepoPermission(RepositoryPermission.Admin);
        
        group.MapGet("/{repoId:guid}/config", GetRepositoryConfigAsync)
            .WithDescription("Get the configuration of a repository.")
            .WithSummary("Get Repository Config")
            .Produces<RepositoryConfigDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireRepoPermission(RepositoryPermission.Read);
        
        group.MapGet("/{repoId:guid}/releases", GetReleasesForRepositoryAsync)
            .WithDescription("Get all releases for a repository.")
            .WithSummary("Get Repository Releases")
            .Produces<IEnumerable<ReleaseSummaryDto>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireRepoPermission(RepositoryPermission.Read);
    }

    private static async Task<IResult> SetRepositoryMemberAccessInfosAsync(Guid repoId, RepositoryAccessDto request, HttpContext context, TenantContext tenantContext, BinStashDbContext db)
    {
        var repo = await db.Repositories.Where(r => r.TenantId == tenantContext.TenantId).FirstOrDefaultAsync(r => r.Id == repoId);
        if (repo == null)
            return Results.NotFound();

        var roleAssignment = await db.RepositoryRoleAssignments
            .FirstOrDefaultAsync(x => x.RepositoryId == repo.Id && x.SubjectType == (SubjectType)request.SubjectType && x.SubjectId == request.SubjectId);

        if (roleAssignment != null)
        {
            roleAssignment.RoleName = request.Role;
            roleAssignment.GrantedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            roleAssignment = new RepositoryRoleAssignment
            {
                RepositoryId = repo.Id,
                SubjectType = (SubjectType)request.SubjectType,
                SubjectId = request.SubjectId,
                RoleName = request.Role,
                GrantedAt = DateTimeOffset.UtcNow
            };
            await db.RepositoryRoleAssignments.AddAsync(roleAssignment);
        }

        await db.SaveChangesAsync();

        return Results.Ok(new RepositoryAccessDto((short)roleAssignment.SubjectType, roleAssignment.SubjectId, roleAssignment.RoleName, roleAssignment.GrantedAt));
    }

    private static async Task<IResult> CreateRepositoryAsync(CreateRepositoryDto dto, BinStashDbContext db, TenantContext tenantContext)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Results.BadRequest("Repository name is required.");
            
        if (db.Repositories.Any(x => x.Name == dto.Name))
            return Results.Conflict($"A repository with the name '{dto.Name}' already exists.");
        
        var allowedStorageClasses = await db.StorageClassMappings.Where(x => x.TenantId == tenantContext.TenantId && x.IsEnabled).ToListAsync();
        
        if (string.IsNullOrWhiteSpace(dto.StorageClassName))
        {
            var defaultStorageClass = allowedStorageClasses.FirstOrDefault(x => x.IsDefault);
            if (defaultStorageClass == null)
                return Results.BadRequest("No default storage class is configured for this tenant. Please specify a storage class.");
            dto.StorageClassName = defaultStorageClass.StorageClassName;
        }
        else
        {
            if (allowedStorageClasses.All(x => x.StorageClassName != dto.StorageClassName))
                return Results.BadRequest($"No storage class with the name '{dto.StorageClassName}' was found in this tenant.");
        }
        
        var storageClass = await db.StorageClassMappings.FirstOrDefaultAsync(x => x.StorageClassName == dto.StorageClassName);
        if (storageClass == null)
            return Results.NotFound($"Storage class with name '{dto.StorageClassName}' not found.");
        
        var chunkStore = await db.ChunkStores.FindAsync(storageClass.ChunkStoreId);
        if (chunkStore == null)
            return Results.NotFound($"Chunk store with ID '{storageClass.ChunkStoreId}' not found.");

        var repo = new Repository
        {
            Name = dto.Name,
            ChunkStore = chunkStore,
            ChunkStoreId = chunkStore.Id,
            Description = dto.Description,
            TenantId = tenantContext.TenantId,
            StorageClass = storageClass.StorageClassName,
            CreatedAt =  DateTimeOffset.UtcNow
        };
            
        await db.Repositories.AddAsync(repo);
            
        await db.SaveChangesAsync();
            
        return Results.Created($"/api/repositories/{repo.Id}", new RepositorySummaryDto
        {
            Id = repo.Id,
            Name = chunkStore.Name,
            Description = repo.Description,
            StorageClass = storageClass.StorageClassName,
            Chunker = new ChunkStoreChunkerDto
            {
                Type = chunkStore.ChunkerOptions.Type.ToString(),
                MinChunkSize = chunkStore.ChunkerOptions.MinChunkSize,
                AvgChunkSize = chunkStore.ChunkerOptions.AvgChunkSize,
                MaxChunkSize = chunkStore.ChunkerOptions.MaxChunkSize,
            }
        });
    }
    
    private static async Task<IResult> ListRepositoriesAsync(BinStashDbContext db, TenantContext tenantContext)
    {
        var repos = await db.Repositories
            .Where(r => r.TenantId == tenantContext.TenantId)
            .Select(r => new RepositorySummaryDto
            {
                Id = r.Id,
                Name = r.Name,
                StorageClass = r.StorageClass,
                Description = r.Description
            })
            .ToListAsync();

        return Results.Ok(repos);
    }
    
    private static async Task<IResult> GetRepositoryByIdAsync(Guid repoId, BinStashDbContext db, TenantContext tenantContext)
    {
        var repo = await db.Repositories.Include(r => r.ChunkStore).Where(r => r.TenantId == tenantContext.TenantId).FirstOrDefaultAsync(r => r.Id == repoId);
        if (repo == null)
            return Results.NotFound();

        return Results.Ok(new RepositorySummaryDto
        {
            Id = repo.Id,
            Name = repo.Name,
            Description = repo.Description,
            StorageClass = repo.StorageClass,
            Chunker = new ChunkStoreChunkerDto
            {
                Type = repo.ChunkStore.ChunkerOptions.Type.ToString(),
                MinChunkSize = repo.ChunkStore.ChunkerOptions.MinChunkSize,
                AvgChunkSize = repo.ChunkStore.ChunkerOptions.AvgChunkSize,
                MaxChunkSize = repo.ChunkStore.ChunkerOptions.MaxChunkSize,
            }
        });
    }
    
    private static async Task<IResult> GetRepositoryMemberAccessInfosAsync(Guid repoId, BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        var repo = await db.Repositories.Where(r => r.TenantId == tenantContext.TenantId).FirstOrDefaultAsync(r => r.Id == repoId);
        if (repo == null)
            return Results.NotFound();

        var accessInfos = await db.RepositoryRoleAssignments.Where(x => x.RepositoryId == repo.Id).Select(x => new RepositoryAccessDto((short)x.SubjectType, x.SubjectId, x.RoleName, x.GrantedAt)).ToListAsync();
        var tenantAdmins = await db.TenantRoleAssignments.Where(x => x.TenantId == tenantContext.TenantId && x.RoleName == "TenantAdmin").Select(x => new RepositoryAccessDto((short)SubjectType.User, x.UserId, "TenantAdmin", x.GrantedAt)).ToListAsync();
        
        accessInfos.AddRange(tenantAdmins);

        return Results.Ok(accessInfos);
    }
    
    private static async Task<IResult> DeleteRepositoryMemberAccessAsync(Guid repoId, SubjectType subjectType, Guid subjectId, BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        var repo = await db.Repositories.Where(r => r.TenantId == tenantContext.TenantId).FirstOrDefaultAsync(r => r.Id == repoId);
        if (repo == null)
            return Results.NotFound();
        
        var roleAssignment = await db.RepositoryRoleAssignments.FirstOrDefaultAsync(x => x.RepositoryId == repo.Id && x.SubjectType == subjectType && x.SubjectId == subjectId);
        if (roleAssignment == null)
            return Results.NotFound();
        
        db.RepositoryRoleAssignments.Remove(roleAssignment);
        await db.SaveChangesAsync();
        
        return Results.NoContent();
    }
    
    private static async Task<IResult> GetRepositoryConfigAsync(Guid repoId, BinStashDbContext db, TenantContext tenantContext)
    {
        var repo = await db.Repositories.AsNoTracking().Include(x => x.ChunkStore).FirstOrDefaultAsync(r => r.TenantId == tenantContext.TenantId && r.Id == repoId);
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

    private static async Task<IResult> GetReleasesForRepositoryAsync(Guid repoId, BinStashDbContext db, TenantContext tenantContext)
    {
        var repo = await db.Repositories.AsNoTracking().Include(x => x.Releases).FirstOrDefaultAsync(r => r.TenantId == tenantContext.TenantId && r.Id == repoId);
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
                StorageClass = repo.StorageClass
            }
        }));
    }

}