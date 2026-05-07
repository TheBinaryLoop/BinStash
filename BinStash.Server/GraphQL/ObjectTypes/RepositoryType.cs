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

using BinStash.Core.Auth.Repository;
using BinStash.Server.GraphQL.Auth;
using BinStash.Server.GraphQL.Services;
using HotChocolate.Types.Pagination;
using Microsoft.AspNetCore.Authorization;

namespace BinStash.Server.GraphQL.ObjectTypes;

public sealed class RepositoryType : ObjectType<RepositoryGql>
{
    protected override void Configure(IObjectTypeDescriptor<RepositoryGql> descriptor)
    {
        descriptor.Field("releases")
            .Authorize()
            .ResolveWith<Resolvers>(x => x.GetReleases(null!, null!, null!, null!))
            .UsePaging<ReleaseType>(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseProjection()
            .UseFiltering()
            .UseSorting();
    }

    private sealed class Resolvers
    {
        public async Task<IQueryable<ReleaseGql>> GetReleases(
            [Parent] RepositoryGql repository,
            [Service] RepositoryQueryService repositoryQueryService,
            [Service] IHttpContextAccessor httpContextAccessor,
            [Service] IAuthorizationService authorizationService)
        {
            var tenantContext = GraphQlAuth.EnsureTenantResolved(httpContextAccessor);

            var user = httpContextAccessor.HttpContext?.User
                       ?? throw new GraphQLException("No user context.");

            await GraphQlAuth.EnsureRepositoryPermissionAsync(
                user,
                authorizationService,
                tenantContext.TenantId,
                repository.Id,
                RepositoryPermission.Read);

            return repositoryQueryService.GetReleasesForRepository(repository.Id);
        }
    }
}