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
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

using BinStash.Core.Auditing;

namespace BinStash.Server.GraphQL.Features.Repositories;

public sealed class RepositoryMutationService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuditLogWriter _audit;

    public RepositoryMutationService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService, IAuditLogWriter audit)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
        _audit = audit;
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

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.RepositoryCreated,
            TargetType = nameof(Repository),
            TargetId = repo.Id.ToString(),
            TargetName = repo.Name,
            Metadata = new Dictionary<string, object?>
            {
                ["storageClass"] = repo.StorageClass,
                ["chunkStoreId"] = repo.ChunkStoreId
            }
        }, ct);

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

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.RepositoryUpdated,
            TargetType = nameof(Repository),
            TargetId = repo.Id.ToString(),
            TargetName = repo.Name
        }, ct);

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

    /// <summary>
    /// Moves a repository into another tenant.
    /// </summary>
    /// <remarks>
    /// The move is metadata-only. Chunks, packs and file definitions are addressed by
    /// <see cref="Repository.ChunkStoreId"/>, which is not tenant-scoped, so nothing on disk has to
    /// change — but only as long as the target tenant is actually configured for that same chunk
    /// store. A target without a matching storage class is rejected rather than silently re-pointed,
    /// because re-pointing would orphan every existing release.
    /// </remarks>
    public async Task<RepositoryGql> MoveRepositoryAsync(MoveRepositoryInput input, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        // Tenant admin, not repository admin: moving a repository out changes what the tenant is
        // billed for and who can reach its data, which is above a per-repository grant's pay grade.
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        if (input.TargetTenantId == tenantContext.TenantId)
            throw new GraphQLException("The repository is already in this workspace.");

        var repo = await _db.Repositories
            .Include(r => r.ChunkStore)
            .FirstOrDefaultAsync(r => r.TenantId == tenantContext.TenantId && r.Id == input.RepoId, ct);

        if (repo is null)
            throw new GraphQLException("Repository not found.");

        // ...and admin on the receiving side too, so a repository can never be pushed into a
        // workspace the caller does not administer. Instance admins satisfy both checks.
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, input.TargetTenantId, TenantPermission.Admin);

        var targetTenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == input.TargetTenantId, ct);

        if (targetTenant is null)
            throw new GraphQLException("Target workspace not found.");

        if (targetTenant.Status != TenantStatus.Active)
            throw new GraphQLException($"Workspace '{targetTenant.Name}' is not active.");

        if (await _db.Repositories.AnyAsync(r => r.TenantId == targetTenant.Id && r.Name == repo.Name, ct))
            throw new GraphQLException($"A repository named '{repo.Name}' already exists in workspace '{targetTenant.Name}'.");

        var compatibleClasses = await _db.StorageClassMappings
            .AsNoTracking()
            .Where(m => m.TenantId == targetTenant.Id && m.IsEnabled && m.ChunkStoreId == repo.ChunkStoreId)
            .ToListAsync(ct);

        if (compatibleClasses.Count == 0)
            throw new GraphQLException(
                $"Workspace '{targetTenant.Name}' has no enabled storage class backed by chunk store " +
                $"'{repo.ChunkStore.Name}', so this repository cannot be moved there without copying its data.");

        StorageClassMapping targetClass;

        if (!string.IsNullOrWhiteSpace(input.StorageClassName))
        {
            targetClass = compatibleClasses.FirstOrDefault(x => x.StorageClassName == input.StorageClassName)
                ?? throw new GraphQLException(
                    $"Storage class '{input.StorageClassName}' is not available in workspace " +
                    $"'{targetTenant.Name}' for this repository's chunk store.");
        }
        else
        {
            // Keep the repository's current label where the target offers it, then its default,
            // then any compatible class — ordered so the pick is deterministic either way.
            targetClass = compatibleClasses.FirstOrDefault(x => x.StorageClassName == repo.StorageClass)
                ?? compatibleClasses.FirstOrDefault(x => x.IsDefault)
                ?? compatibleClasses.OrderBy(x => x.StorageClassName, StringComparer.Ordinal).First();
        }

        // Per-repository grants name users, groups and service accounts of the source tenant.
        // Carrying them across would hand someone access inside a workspace that never granted them
        // a role, so the repository arrives with an empty ACL for the new admins to rebuild.
        var staleGrants = await _db.RepositoryRoleAssignments
            .Where(a => a.RepositoryId == repo.Id)
            .ToListAsync(ct);

        if (staleGrants.Count > 0)
            _db.RepositoryRoleAssignments.RemoveRange(staleGrants);

        var sourceTenantId = repo.TenantId;
        var sourceStorageClass = repo.StorageClass;

        var sourceTenantName = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == sourceTenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        repo.TenantId = targetTenant.Id;
        repo.StorageClass = targetClass.StorageClassName;

        await _db.SaveChangesAsync(ct);

        var metadata = new Dictionary<string, object?>
        {
            ["fromTenantId"] = sourceTenantId,
            ["fromTenantName"] = sourceTenantName,
            ["toTenantId"] = targetTenant.Id,
            ["toTenantName"] = targetTenant.Name,
            ["fromStorageClass"] = sourceStorageClass,
            ["toStorageClass"] = targetClass.StorageClassName,
            ["revokedGrants"] = staleGrants.Count
        };

        // Both sides get the record: the source tenant's log would otherwise show a repository
        // simply vanishing, and the target's would show one appearing from nowhere.
        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.RepositoryMoved,
            TargetType = nameof(Repository),
            TargetId = repo.Id.ToString(),
            TargetName = repo.Name,
            TenantId = sourceTenantId,
            Metadata = metadata
        }, ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.RepositoryMoved,
            TargetType = nameof(Repository),
            TargetId = repo.Id.ToString(),
            TargetName = repo.Name,
            TenantId = targetTenant.Id,
            Metadata = metadata
        }, ct);

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

    public async Task<RepositoryAccessGql> GrantRepositoryAccessAsync(Guid repoId, short subjectType, Guid subjectId, string role, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, repoId, RepositoryPermission.Admin);

        if (string.IsNullOrWhiteSpace(role))
            throw new GraphQLException("A role is required.");

        var repoExists = await _db.Repositories.AnyAsync(r => r.TenantId == tenantContext.TenantId && r.Id == repoId, ct);
        if (!repoExists)
            throw new GraphQLException("Repository not found.");

        var subject = (SubjectType)subjectType;

        var roleAssignment = await _db.RepositoryRoleAssignments
            .FirstOrDefaultAsync(x => x.RepositoryId == repoId && x.SubjectType == subject && x.SubjectId == subjectId, ct);

        if (roleAssignment is not null)
        {
            roleAssignment.RoleName = role;
            roleAssignment.GrantedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            roleAssignment = new RepositoryRoleAssignment
            {
                RepositoryId = repoId,
                SubjectType = subject,
                SubjectId = subjectId,
                RoleName = role,
                GrantedAt = DateTimeOffset.UtcNow
            };
            await _db.RepositoryRoleAssignments.AddAsync(roleAssignment, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.RepositoryAccessGranted,
            TargetType = nameof(Repository),
            TargetId = repoId.ToString(),
            Metadata = new Dictionary<string, object?>
            {
                ["subjectType"] = subject.ToString(),
                ["subjectId"] = subjectId,
                ["role"] = role
            }
        }, ct);

        return new RepositoryAccessGql
        {
            SubjectType = (short)roleAssignment.SubjectType,
            SubjectId = roleAssignment.SubjectId,
            Role = roleAssignment.RoleName,
            GrantedAt = roleAssignment.GrantedAt
        };
    }

    public async Task<bool> RevokeRepositoryAccessAsync(Guid repoId, short subjectType, Guid subjectId, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, repoId, RepositoryPermission.Admin);

        var subject = (SubjectType)subjectType;

        var roleAssignment = await _db.RepositoryRoleAssignments
            .FirstOrDefaultAsync(x => x.RepositoryId == repoId && x.SubjectType == subject && x.SubjectId == subjectId, ct);

        if (roleAssignment is null)
            return false;

        _db.RepositoryRoleAssignments.Remove(roleAssignment);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.RepositoryAccessRevoked,
            TargetType = nameof(Repository),
            TargetId = repoId.ToString(),
            Metadata = new Dictionary<string, object?>
            {
                ["subjectType"] = subject.ToString(),
                ["subjectId"] = subjectId,
                ["role"] = roleAssignment.RoleName
            }
        }, ct);

        return true;
    }
}