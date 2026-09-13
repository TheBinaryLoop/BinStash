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
using BinStash.Core.Auth.Tenant;
using Microsoft.AspNetCore.Authorization;

namespace BinStash.Server.GraphQL.Auth;

/// <summary>
/// Declarative authorization for schema fields.
/// </summary>
/// <remarks>
/// Tenant/instance permission checks used to live only inside the resolver service bodies, which
/// meant a resolver that forgot to call <see cref="GraphQlAuth"/> was silently unauthorized. These
/// extensions move the requirement next to the field declaration in
/// <see cref="QueryType"/>/<see cref="MutationType"/>, so the whole authorization surface of the
/// schema is auditable in one place.
///
/// The checks inside the resolver services are deliberately kept as defence in depth: a field
/// reached through a nested path still re-verifies its own preconditions.
///
/// These must be declared BEFORE paging/projection middleware on a field so that they wrap it and
/// run first.
/// </remarks>
public static class AuthorizationDescriptorExtensions
{
    public static IObjectFieldDescriptor RequireTenantPermission(
        this IObjectFieldDescriptor descriptor,
        TenantPermission permission)
        => descriptor
            .Authorize()
            .Use(next => async context =>
            {
                var httpContextAccessor = context.Services.GetRequiredService<IHttpContextAccessor>();
                var authorizationService = context.Services.GetRequiredService<IAuthorizationService>();

                var tenantContext = GraphQlAuth.EnsureTenantResolved(httpContextAccessor);
                var user = RequireUser(httpContextAccessor);

                await GraphQlAuth.EnsureTenantPermissionAsync(
                    user, authorizationService, tenantContext.TenantId, permission);

                await next(context);
            });

    public static IObjectFieldDescriptor RequireInstancePermission(
        this IObjectFieldDescriptor descriptor,
        InstancePermission permission)
        => descriptor
            .Authorize()
            .Use(next => async context =>
            {
                var httpContextAccessor = context.Services.GetRequiredService<IHttpContextAccessor>();
                var authorizationService = context.Services.GetRequiredService<IAuthorizationService>();

                await GraphQlAuth.EnsureInstancePermissionAsync(
                    RequireUser(httpContextAccessor), authorizationService, permission);

                await next(context);
            });

    private static System.Security.Claims.ClaimsPrincipal RequireUser(IHttpContextAccessor httpContextAccessor)
        => httpContextAccessor.HttpContext?.User
           ?? throw new GraphQLException(ErrorBuilder.New()
               .SetMessage("No user context.")
               .SetCode("UNAUTHENTICATED")
               .Build());
}
