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
using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Server.Auth;
using BinStash.Server.Context;
using Microsoft.AspNetCore.Authorization;

namespace BinStash.Server.GraphQL.Auth;

public static class GraphQlAuth
{
    public static async Task EnsureInstancePermissionAsync(ClaimsPrincipal user, IAuthorizationService authorizationService, InstancePermission permission)
    {
        var result = await AuthChecker.CheckInstancePermissionAsync(user, authorizationService, permission);
        if (!result.Succeeded)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Forbidden.")
                .SetCode("FORBIDDEN")
                .Build());
    }

    
    public static async Task EnsureTenantPermissionAsync(ClaimsPrincipal user, IAuthorizationService authorizationService, Guid tenantId, TenantPermission permission)
    {
        var result = await AuthChecker.CheckTenantPermissionAsync(user, authorizationService, tenantId, permission);
        if (!result.Succeeded)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Forbidden.")
                .SetCode("FORBIDDEN")
                .Build());
    }

    public static async Task EnsureRepositoryPermissionAsync(ClaimsPrincipal user, IAuthorizationService authorizationService, Guid tenantId, Guid repoId, RepositoryPermission permission)
    {
        var result = await AuthChecker.CheckRepositoryPermissionAsync(user, authorizationService, tenantId, repoId, permission);
        if (!result.Succeeded)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Forbidden.")
                .SetCode("FORBIDDEN")
                .Build());
    }

    public static ITenantContext EnsureTenantResolved(IHttpContextAccessor httpContextAccessor)
    {
        var tenantContext = httpContextAccessor.HttpContext!
            .RequestServices
            .GetRequiredService<ITenantContext>();
        
        if (!tenantContext.IsResolved)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Tenant context is missing.")
                .SetCode("TENANT_CONTEXT_MISSING")
                .Build());
        }

        return tenantContext;
    }
}