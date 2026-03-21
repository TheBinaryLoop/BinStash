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

using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using BinStash.Server.GraphQL.Inputs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Services;

public sealed class RepositoryMutationService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public RepositoryMutationService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }
    
    public async Task<RepositoryGql> CreateRepositoryAsync(CreateRepositoryInput input, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        if (string.IsNullOrWhiteSpace(input.Name))
            throw new GraphQLException("Repository name is required.");

        if (await _db.Repositories.AnyAsync(x => x.TenantId == tenantContext.TenantId && x.Name == input.Name, ct))
        {
            throw new GraphQLException($"A repository with the name '{input.Name}' already exists.");
        }

        var allowedStorageClasses = await _db.StorageClassMappings
            .Where(x => x.TenantId == tenantContext.TenantId && x.IsEnabled)
            .ToListAsync(ct);

        var storageClassName = input.StorageClassName;

        if (string.IsNullOrWhiteSpace(storageClassName))
        {
            var defaultStorageClass = allowedStorageClasses.FirstOrDefault(x => x.IsDefault);
            if (defaultStorageClass is null)
                throw new GraphQLException("No default storage class is configured for this tenant. Please specify a storage class.");

            storageClassName = defaultStorageClass.StorageClassName;
        }
        else
        {
            if (allowedStorageClasses.All(x => x.StorageClassName != storageClassName))
                throw new GraphQLException($"No storage class with the name '{storageClassName}' was found in this tenant.");
        }

        var storageClass = await _db.StorageClassMappings.FirstOrDefaultAsync(x => x.StorageClassName == storageClassName, ct);

        if (storageClass is null)
            throw new GraphQLException($"Storage class with name '{storageClassName}' not found.");

        var chunkStore = await _db.ChunkStores.FindAsync([storageClass.ChunkStoreId], ct);
        if (chunkStore is null)
            throw new GraphQLException($"Chunk store with ID '{storageClass.ChunkStoreId}' not found.");

        var repo = new Repository
        {
            Name = input.Name,
            Description = input.Description,
            ChunkStore = chunkStore,
            ChunkStoreId = chunkStore.Id,
            TenantId = tenantContext.TenantId,
            StorageClass = storageClass.StorageClassName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _db.Repositories.AddAsync(repo, ct);
        await _db.SaveChangesAsync(ct);

        return new RepositoryGql
        {
            Id = repo.Id,
            Name = repo.Name,
            Description = repo.Description,
            StorageClass = repo.StorageClass,
            CreatedAt = repo.CreatedAt,
            Chunker = new ChunkStoreChunkerGql
            {
                Type = chunkStore.ChunkerOptions.Type.ToString(),
                MinChunkSize = chunkStore.ChunkerOptions.MinChunkSize,
                AvgChunkSize = chunkStore.ChunkerOptions.AvgChunkSize,
                MaxChunkSize = chunkStore.ChunkerOptions.MaxChunkSize
            }
        };
    }
    
    public async Task<RepositoryGql> UpdateRepositoryAsync(UpdateRepositoryInput input, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, input.RepoId, RepositoryPermission.Admin);

        var repo = await _db.Repositories
            .Include(r => r.ChunkStore)
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantContext.TenantId && r.Id == input.RepoId,
                ct);

        if (repo is null)
            throw new GraphQLException("Repository not found.");

        if (input.Name.HasValue)
        {
            var newName = input.Name.Value;

            if (string.IsNullOrWhiteSpace(newName))
                throw new GraphQLException("Repository name cannot be empty.");

            var duplicateExists = await _db.Repositories.AnyAsync(
                x => x.TenantId == tenantContext.TenantId &&
                     x.Id != input.RepoId &&
                     x.Name == newName,
                ct);

            if (duplicateExists)
                throw new GraphQLException($"A repository with the name '{newName}' already exists.");

            repo.Name = newName;
        }

        if (input.Description.HasValue)
        {
            // null means: clear description
            repo.Description = input.Description.Value;
        }

        await _db.SaveChangesAsync(ct);

        return new RepositoryGql
        {
            Id = repo.Id,
            Name = repo.Name,
            Description = repo.Description,
            StorageClass = repo.StorageClass,
            CreatedAt = repo.CreatedAt,
            Chunker = new ChunkStoreChunkerGql
            {
                Type = repo.ChunkStore.ChunkerOptions.Type.ToString(),
                MinChunkSize = repo.ChunkStore.ChunkerOptions.MinChunkSize,
                AvgChunkSize = repo.ChunkStore.ChunkerOptions.AvgChunkSize,
                MaxChunkSize = repo.ChunkStore.ChunkerOptions.MaxChunkSize
            }
        };
    }
}