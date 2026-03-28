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
using BinStash.Server.Auth.Repository;
using BinStash.Server.Auth.Tenant;
using Microsoft.AspNetCore.Authorization;

namespace BinStash.Server.Auth;

public static class AuthChecker
{
    public static async Task<AuthorizationResult> CheckInstancePermissionAsync(ClaimsPrincipal user, IAuthorizationService authorizationService, InstancePermission permission)
    {
        var policyName = permission switch
        {
            InstancePermission.Admin => "Permission:Instance:Admin",
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null)
        };

        return await authorizationService.AuthorizeAsync(user, null, policyName);
    }

    
    public static async Task<AuthorizationResult> CheckTenantPermissionAsync(ClaimsPrincipal user, IAuthorizationService authorizationService, Guid tenantId, TenantPermission permission)
    {
        var policyName = permission switch
        {
            TenantPermission.Member => "Permission:Tenant:Member",
            TenantPermission.Admin => "Permission:Tenant:Admin",
            TenantPermission.BillingAdmin => "Permission:Tenant:BillingAdmin",
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null)
        };

        return await authorizationService.AuthorizeAsync(user, new TenantAuthResource(tenantId), policyName);
    }

    public static async Task<AuthorizationResult> CheckRepositoryPermissionAsync(ClaimsPrincipal user, IAuthorizationService authorizationService, Guid tenantId, Guid repoId, RepositoryPermission permission)
    {
        var policyName = permission switch
        {
            RepositoryPermission.Read => "Permission:Repo:Read",
            RepositoryPermission.Write => "Permission:Repo:Write",
            RepositoryPermission.Admin => "Permission:Repo:Admin",
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null)
        };

        return await authorizationService.AuthorizeAsync(user, new RepositoryAuthResource(tenantId, repoId), policyName);
    }
}