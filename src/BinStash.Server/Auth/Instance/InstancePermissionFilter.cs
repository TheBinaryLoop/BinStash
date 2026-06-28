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

using BinStash.Core.Auth.Instance;
using Microsoft.AspNetCore.Authorization;

namespace BinStash.Server.Auth.Instance;

public class InstancePermissionFilter(InstancePermission permission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        
        // Must be authenticated (.RequireAuthorization already enforces this)
        if (http.User.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        
        // Authorize using policy + handler
        var auth = http.RequestServices.GetRequiredService<IAuthorizationService>();
        
        var policyName = permission switch
        {
            /*InstancePermission.Read => "Permission:Instance:Read",
            InstancePermission.Write => "Permission:Instance:Write",*/
            InstancePermission.Admin => "Permission:Instance:Admin",
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null)
        };
        
        var result = await auth.AuthorizeAsync(http.User, null, policyName);
        if (!result.Succeeded)
            return Results.Forbid();
        
        return await next(context);
    }
}