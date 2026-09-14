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

using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Server.Auth.Ingest;
using BinStash.Server.Auth.Instance;
using BinStash.Server.Auth.Repository;
using BinStash.Server.Auth.Tenant;
using Microsoft.AspNetCore.Http.Metadata;

namespace BinStash.Server.Extensions;

/// <summary>
/// Caps the request body for one endpoint. Honoured by routing before the endpoint runs, so the
/// body is refused as it arrives rather than after it has been buffered.
/// </summary>
/// <remarks>
/// Needed because an endpoint otherwise inherits the server-wide ceiling, which has to stay large
/// enough for batched chunk upload and the multipart finalize. A login handler inheriting that
/// means an unauthenticated caller can make the server read tens of megabytes per connection.
/// </remarks>
internal sealed class SmallRequestBodyMetadata(long maxBytes) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize { get; } = maxBytes;
}

public static class EndpointConventionBuilderExtensions
{
    extension(IEndpointConventionBuilder builder)
    {
        public IEndpointConventionBuilder RequireInstancePermission(InstancePermission permisssion)
            => builder.AddEndpointFilter(new InstancePermissionFilter(permisssion));
        
        public IEndpointConventionBuilder RequireRepoPermission(RepositoryPermission permission)
            => builder.AddEndpointFilter(new RepositoryPermissionFilter(permission));

        public IEndpointConventionBuilder RequireTenantPermission(TenantPermission permission)
            => builder.AddEndpointFilter(new TenantPermissionFilter(permission));

        public IEndpointConventionBuilder RequireValidIngestSession()
            => builder.AddEndpointFilter(new IngestSessionBelongsToRepoFilter());

        public IEndpointConventionBuilder RequireSmallRequestBody(long maxBytes)
            => builder.WithMetadata(new SmallRequestBodyMetadata(maxBytes));
    }
    
    extension(RouteHandlerBuilder builder)
    {
        public RouteHandlerBuilder RequireInstancePermission(InstancePermission permisssion)
            => builder.AddEndpointFilter(new InstancePermissionFilter(permisssion));
        
        public RouteHandlerBuilder RequireRepoPermission(RepositoryPermission permission)
            => builder.AddEndpointFilter(new RepositoryPermissionFilter(permission));

        public RouteHandlerBuilder RequireTenantPermission(TenantPermission permission)
            => builder.AddEndpointFilter(new TenantPermissionFilter(permission));

        public RouteHandlerBuilder RequireValidIngestSession()
            => builder.AddEndpointFilter(new IngestSessionBelongsToRepoFilter());

        public RouteHandlerBuilder RequireSmallRequestBody(long maxBytes)
            => builder.WithMetadata(new SmallRequestBodyMetadata(maxBytes));
    }
    
    extension(RouteGroupBuilder builder)
    {
        public RouteGroupBuilder RequireInstancePermission(InstancePermission permisssion)
            => builder.AddEndpointFilter(new InstancePermissionFilter(permisssion));
        
        public RouteGroupBuilder RequireRepoPermission(RepositoryPermission permission)
            => builder.AddEndpointFilter(new RepositoryPermissionFilter(permission));

        public RouteGroupBuilder RequireTenantPermission(TenantPermission permission)
            => builder.AddEndpointFilter(new TenantPermissionFilter(permission));

        public RouteGroupBuilder RequireValidIngestSession()
            => builder.AddEndpointFilter(new IngestSessionBelongsToRepoFilter());

        public RouteGroupBuilder RequireSmallRequestBody(long maxBytes)
            => builder.WithMetadata(new SmallRequestBodyMetadata(maxBytes));
    }
}