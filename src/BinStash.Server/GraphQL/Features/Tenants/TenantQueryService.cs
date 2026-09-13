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

namespace BinStash.Server.GraphQL.Features.Tenants;

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

        // Service-account (machine) subjects have no user/membership row; resolve the single
        // tenant the service account belongs to so CLI/CI flows can map --tenant <slug> to an id.
        if (user.FindFirstValue("auth_type") == "machine")
        {
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var subjectId))
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Unauthorized.")
                        .SetCode("UNAUTHORIZED")
                        .Build());

            return _db.ServiceAccounts
                .AsNoTracking()
                .Where(sa => sa.Id == subjectId)
                .Join(_db.Tenants.AsNoTracking(), sa => sa.TenantId, t => t.Id, (_, t) => new TenantGql
                {
                    Id = t.Id,
                    Name = t.Name,
                    Slug = t.Slug,
                    CreatedAt = t.CreatedAt,
                    JoinedAt = null,
                });
        }

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

    public async Task<List<TenantMemberGql>> GetTenantMembersAsync(CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        var members = await _db.TenantMembers.AsNoTracking()
            .Where(tm => tm.TenantId == tenantContext.TenantId)
            .Join(_db.Users.AsNoTracking(), tm => tm.UserId, u => u.Id, (tm, u) => new { u.Id, u.Email, u.FirstName, u.LastName, tm.JoinedAt })
            .ToListAsync(ct);

        var memberRoles = await _db.TenantRoleAssignments.AsNoTracking()
            .Where(t => t.TenantId == tenantContext.TenantId)
            .ToListAsync(ct);

        return members.Select(m => new TenantMemberGql
        {
            Id = m.Id,
            Email = m.Email ?? string.Empty,
            FirstName = m.FirstName,
            LastName = m.LastName,
            JoinedAt = m.JoinedAt,
            Roles = memberRoles.Where(r => r.UserId == m.Id).Select(r => r.RoleName).ToList()
        }).ToList();
    }

    public async Task<List<TenantStorageClassGql>> GetTenantStorageClassesAsync(CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Member);

        return await _db.StorageClassMappings.AsNoTracking()
            .Where(scm => scm.TenantId == tenantContext.TenantId && scm.IsEnabled)
            .Join(_db.StorageClasses.AsNoTracking(),
                scm => scm.StorageClassName,
                sc => sc.Name,
                (scm, sc) => new TenantStorageClassGql
                {
                    Name = sc.Name,
                    Description = sc.Description,
                    IsDefault = scm.IsDefault
                })
            .ToListAsync(ct);
    }

    public async Task<TenantInvitationPreviewGql?> GetTenantInvitationPreviewAsync(Guid tenantId, string code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
            throw new GraphQLException("Invitation code is required.");

        string decodedCode;
        try
        {
            var decodedBytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(code);
            decodedCode = System.Text.Encoding.UTF8.GetString(decodedBytes);
        }
        catch
        {
            throw new GraphQLException("Invalid invitation code.");
        }

        var invitation = await _db.TenantMemberInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Code == decodedCode && i.ExpiresAt > DateTimeOffset.UtcNow && i.AcceptedAt == null, ct);
        if (invitation is null)
            return null;

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
            return null;

        return new TenantInvitationPreviewGql
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            TenantSlug = tenant.Slug,
            Role = invitation.Roles.First(),
            InvitedEmail = invitation.InviteeEmail,
            ExpiresAt = invitation.ExpiresAt
        };
    }
}