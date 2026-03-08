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
using BinStash.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Auth.Instance;

public class InstancePermissionHandler(BinStashDbContext db) : AuthorizationHandler<InstancePermissionRequirement>
{
    private static Guid _instanceAdminRoleId = Guid.Empty;
    
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, InstancePermissionRequirement requirement)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return;

        // Add more permissions here

        if (_instanceAdminRoleId == Guid.Empty)
            _instanceAdminRoleId = await db.Roles.Where(r => r.Name == "InstanceAdmin").Select(r => r.Id).FirstOrDefaultAsync();
        var isAdmin = await db.UserRoles.Where(x => x.UserId == userId && x.RoleId == _instanceAdminRoleId).AnyAsync();
        if (isAdmin)
            context.Succeed(requirement);
    }
}