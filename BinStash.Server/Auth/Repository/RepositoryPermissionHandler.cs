// Copyright (C) 2025  Lukas Eßmann
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
using BinStash.Core.Auth;
using BinStash.Core.Auth.Repository;
using BinStash.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Auth.Repository;

public class RepositoryPermissionHandler(BinStashDbContext db) : AuthorizationHandler<RepositoryPermissionRequirement, RepositoryAuthResource>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RepositoryPermissionRequirement requirement, RepositoryAuthResource resource)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return;
        
        // Must be tenant member
        var isMember = await db.TenantMembers.AnyAsync(m => m.TenantId == resource.TenantId && m.UserId == userId);

        if (!isMember)
            return;
        
        // Tenant-admin override
        var isTenantAdmin = await db.TenantRoleAssignments.AnyAsync(r =>
            r.TenantId == resource.TenantId &&
            r.UserId == userId &&
            r.RoleName == "TenantAdmin");
        
        if (isTenantAdmin)
        {
            context.Succeed(requirement);
            return;
        }
        
        // Repo must belong to tenant (this is a cheap safety check)
        var repoInTenant = await db.Repositories
            .AnyAsync(r => r.Id == resource.RepoId && r.TenantId == resource.TenantId);

        if (!repoInTenant)
            return;
        
        // Direct user assignment
        var role = await db.RepositoryRoleAssignments
            .Where(a =>
                a.RepositoryId == resource.RepoId &&
                a.SubjectType == SubjectType.User &&
                a.SubjectId == userId)
            .Select(a => a.RoleName)
            .SingleOrDefaultAsync();
        
        if (role is not null && RepositoryRoles.Allows(role, requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }
        
        // group-based assignment
        var groupIds = await db.UserGroupMembers
            .Join(db.UserGroups, gm => gm.GroupId, g => g.Id, (gm, g) => new { gm, g })
            .Where(x => x.gm.UserId == userId && x.g.TenantId == resource.TenantId)
            .Select(x => x.gm.GroupId)
            .ToListAsync();

        if (groupIds.Count > 0)
        {
            var groupRole = await db.RepositoryRoleAssignments
                .Where(a => a.RepositoryId == resource.RepoId
                            && a.SubjectType == SubjectType.Group
                            && groupIds.Contains(a.SubjectId))
                .Select(a => a.RoleName)
                .FirstOrDefaultAsync();

            if (groupRole is not null && RepositoryRoles.Allows(groupRole, requirement.Permission))
            {
                context.Succeed(requirement);
            }
        }
    }
}