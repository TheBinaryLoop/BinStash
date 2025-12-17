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
using BinStash.Core.Auth.Tenant;
using BinStash.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Auth.Tenant;

public sealed class TenantPermissionHandler(BinStashDbContext db)
    : AuthorizationHandler<TenantPermissionRequirement, TenantAuthResource>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantPermissionRequirement requirement, TenantAuthResource resource)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return;

        // must be a tenant member
        var isMember = await db.TenantMembers
            .AnyAsync(m => m.TenantId == resource.TenantId && m.UserId == userId);

        if (!isMember)
            return;

        if (requirement.Permission == TenantPermission.Member)
        {
            context.Succeed(requirement);
            return;
        }

        // admin required
        var isAdmin = await db.TenantRoleAssignments.AnyAsync(r =>
            r.TenantId == resource.TenantId &&
            r.UserId == userId &&
            r.RoleName == "TenantAdmin");

        if (isAdmin)
            context.Succeed(requirement);
    }
}