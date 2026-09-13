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

using BinStash.Core.Auth;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.Repositories;

public class RepositoryQueryService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public RepositoryQueryService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    public async Task<IQueryable<RepositoryGql>> GetRepositoriesAsync()
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Member);

        return _db.Repositories
            .AsNoTracking()
            .Where(r => r.TenantId == tenantContext.TenantId)
            .Select(r => new RepositoryGql
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                StorageClass = r.StorageClass,
                CreatedAt = r.CreatedAt,
                Chunker = new ChunkStoreChunkerGql
                {
                    Type = r.ChunkStore.ChunkerOptions.Type.ToString(),
                    MinChunkSize = r.ChunkStore.ChunkerOptions.MinChunkSize,
                    AvgChunkSize = r.ChunkStore.ChunkerOptions.AvgChunkSize,
                    MaxChunkSize = r.ChunkStore.ChunkerOptions.MaxChunkSize
                }
            });
    }
    
    public async Task<RepositoryGql?> GetRepositoryByIdAsync(Guid repoId, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, repoId, RepositoryPermission.Read);

        return await _db.Repositories
            .AsNoTracking()
            .Where(r => r.TenantId == tenantContext.TenantId && r.Id == repoId)
            .Select(r => new RepositoryGql
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                StorageClass = r.StorageClass,
                CreatedAt = r.CreatedAt,
                Chunker = new ChunkStoreChunkerGql
                {
                    Type = r.ChunkStore.ChunkerOptions.Type.ToString(),
                    MinChunkSize = r.ChunkStore.ChunkerOptions.MinChunkSize,
                    AvgChunkSize = r.ChunkStore.ChunkerOptions.AvgChunkSize,
                    MaxChunkSize = r.ChunkStore.ChunkerOptions.MaxChunkSize
                }
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RepositoryGql?> GetRepositoryByNameAsync(string repoName, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        var repoId = await _db.Repositories
            .AsNoTracking()
            .Where(r => r.TenantId == tenantContext.TenantId && r.Name == repoName)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        if (repoId is null)
            return null;

        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, repoId.Value, RepositoryPermission.Read);

        return await _db.Repositories
            .AsNoTracking()
            .Where(r => r.TenantId == tenantContext.TenantId && r.Id == repoId.Value)
            .Select(r => new RepositoryGql
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                StorageClass = r.StorageClass,
                CreatedAt = r.CreatedAt,
                Chunker = new ChunkStoreChunkerGql
                {
                    Type = r.ChunkStore.ChunkerOptions.Type.ToString(),
                    MinChunkSize = r.ChunkStore.ChunkerOptions.MinChunkSize,
                    AvgChunkSize = r.ChunkStore.ChunkerOptions.AvgChunkSize,
                    MaxChunkSize = r.ChunkStore.ChunkerOptions.MaxChunkSize
                }
            })
            .FirstOrDefaultAsync(ct);
    }

    public IQueryable<ReleaseGql> GetReleasesForRepository(Guid repoId)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        return _db.Releases
            .AsNoTracking()
            .Where(r => r.RepoId == repoId && r.Repository.TenantId == tenantContext.TenantId)
            .Select(r => new ReleaseGql
            {
                Id = r.Id,
                Version = r.Version,
                CreatedAt = r.CreatedAt,
                Notes = r.Notes,
                RepoId = r.RepoId,
                CustomProperties = null // resolved separately
            });
    }

    public async Task<RepositoryConfigGql> GetRepositoryConfigAsync(Guid repoId)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, repoId, RepositoryPermission.Read);

        var config = await _db.Repositories
            .AsNoTracking()
            .Where(r => r.TenantId == tenantContext.TenantId && r.Id == repoId)
            .Select(r => new RepositoryConfigGql
            {
                DedupeConfig = new RepositoryDedupeConfigGql
                {
                    Chunker = r.ChunkStore.ChunkerOptions.Type.ToString(),
                    MinChunkSize = r.ChunkStore.ChunkerOptions.MinChunkSize,
                    AvgChunkSize = r.ChunkStore.ChunkerOptions.AvgChunkSize,
                    MaxChunkSize = r.ChunkStore.ChunkerOptions.MaxChunkSize,
                    ShiftCount = r.ChunkStore.ChunkerOptions.ShiftCount,
                    BoundaryCheckBytes = r.ChunkStore.ChunkerOptions.BoundaryCheckBytes
                }
            })
            .FirstOrDefaultAsync();

        return config ?? throw new GraphQLException("Repository not found.");
    }

    public async Task<List<RepositoryAccessGql>> GetRepositoryAccessAsync(Guid repoId)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, repoId, RepositoryPermission.Admin);

        var repoExists = await _db.Repositories.AnyAsync(r => r.TenantId == tenantContext.TenantId && r.Id == repoId);
        if (!repoExists)
            throw new GraphQLException("Repository not found.");

        var accessInfos = await _db.RepositoryRoleAssignments
            .AsNoTracking()
            .Where(x => x.RepositoryId == repoId)
            .Select(x => new RepositoryAccessGql
            {
                SubjectType = (short)x.SubjectType,
                SubjectId = x.SubjectId,
                Role = x.RoleName,
                GrantedAt = x.GrantedAt
            })
            .ToListAsync();

        var tenantAdmins = await _db.TenantRoleAssignments
            .AsNoTracking()
            .Where(x => x.TenantId == tenantContext.TenantId && x.RoleName == "TenantAdmin")
            .Select(x => new RepositoryAccessGql
            {
                SubjectType = (short)SubjectType.User,
                SubjectId = x.UserId,
                Role = "TenantAdmin",
                GrantedAt = x.GrantedAt
            })
            .ToListAsync();

        accessInfos.AddRange(tenantAdmins);
        return accessInfos;
    }
}