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

using System.Security.Claims;
using BinStash.Core.Auth.Tenant;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Services;

public class TenantQueryService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public TenantQueryService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    public async Task<TenantGql> GetCurrentTenantAsync()
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Member);
        
        return _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => new TenantGql
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                CreatedAt = t.CreatedAt,
            })
            .FirstOrDefault() ?? throw new GraphQLException("Tenant not found.");
    }
    
    public async Task<IQueryable<TenantGql>> GetTenantsAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new GraphQLException("No user context.");

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        if (!Guid.TryParse(userIdStr, out var userId))
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unauthorized.")
                    .SetCode("UNAUTHORIZED")
                    .Build());

        var isInstanceAdmin = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                _db.Roles.AsNoTracking(),
                ur => ur.RoleId,
                r => r.Id,
                (_, r) => r.Name)
            .AnyAsync(roleName => roleName == "InstanceAdmin");

        if (isInstanceAdmin)
        {
            return _db.Tenants
                .AsNoTracking()
                .Select(t => new TenantGql
                {
                    Id = t.Id,
                    Name = t.Name,
                    Slug = t.Slug,
                    CreatedAt = t.CreatedAt,
                    JoinedAt = null,
                });
        }

        return _db.TenantMembers
            .AsNoTracking()
            .Where(tm => tm.UserId == userId)
            .Join(
                _db.Tenants.AsNoTracking(),
                tm => tm.TenantId,
                t => t.Id,
                (tm, t) => new { tm, t })
            .Select(x => new TenantGql
            {
                Id = x.t.Id,
                Name = x.t.Name,
                Slug = x.t.Slug,
                CreatedAt = x.t.CreatedAt,
                JoinedAt = x.tm.JoinedAt,
            });
    }
    
    public async Task<TenantGql?> GetTenantByIdAsync(Guid repoId, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Member);

        return await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == repoId)
            .Select(t => new TenantGql
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                CreatedAt = t.CreatedAt,
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
}