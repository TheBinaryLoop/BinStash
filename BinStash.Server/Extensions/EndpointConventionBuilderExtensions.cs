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

using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Server.Auth.Ingest;
using BinStash.Server.Auth.Repository;
using BinStash.Server.Auth.Tenant;

namespace BinStash.Server.Extensions;

public static class EndpointConventionBuilderExtensions
{
    extension(IEndpointConventionBuilder builder)
    {
        public IEndpointConventionBuilder RequireRepoPermission(RepositoryPermission permission)
            => builder.AddEndpointFilter(new RepositoryPermissionFilter(permission));

        public IEndpointConventionBuilder RequireTenantPermission(TenantPermission permission)
            => builder.AddEndpointFilter(new TenantPermissionFilter(permission));

        public IEndpointConventionBuilder RequireValidIngestSession()
            => builder.AddEndpointFilter(new IngestSessionBelongsToRepoFilter());
    }
    
    extension(RouteHandlerBuilder builder)
    {
        public RouteHandlerBuilder RequireRepoPermission(RepositoryPermission permission)
            => builder.AddEndpointFilter(new RepositoryPermissionFilter(permission));

        public RouteHandlerBuilder RequireTenantPermission(TenantPermission permission)
            => builder.AddEndpointFilter(new TenantPermissionFilter(permission));

        public RouteHandlerBuilder RequireValidIngestSession()
            => builder.AddEndpointFilter(new IngestSessionBelongsToRepoFilter());
    }
    
    extension(RouteGroupBuilder builder)
    {
        public RouteGroupBuilder RequireRepoPermission(RepositoryPermission permission)
            => builder.AddEndpointFilter(new RepositoryPermissionFilter(permission));

        public RouteGroupBuilder RequireTenantPermission(TenantPermission permission)
            => builder.AddEndpointFilter(new TenantPermissionFilter(permission));

        public RouteGroupBuilder RequireValidIngestSession()
            => builder.AddEndpointFilter(new IngestSessionBelongsToRepoFilter());
    }
}