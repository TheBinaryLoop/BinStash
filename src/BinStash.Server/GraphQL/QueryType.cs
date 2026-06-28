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

using HotChocolate.Types.Pagination;
using BinStash.Server.GraphQL.Features.ChunkStores;
using BinStash.Server.GraphQL.Features.Releases;
using BinStash.Server.GraphQL.Features.Repositories;
using BinStash.Server.GraphQL.Features.ServiceAccounts;
using BinStash.Server.GraphQL.Features.Tenants;

namespace BinStash.Server.GraphQL;

public sealed class QueryType : ObjectType<Query>
{
    protected override void Configure(IObjectTypeDescriptor<Query> descriptor)
    {
        descriptor
            .Field(x => x.GetCurrentTenant(null!))
            .Type<NonNullType<TenantType>>()
            .Authorize()
            .UseProjection();
        
        descriptor
            .Field(x => x.GetTenants(null!))
            .Authorize()
            .UsePaging(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseProjection()
            .UseFiltering()
            .UseSorting();
        
        descriptor
            .Field(x => x.GetTenant(Guid.Empty, null!, CancellationToken.None))
            .Type<TenantType>()
            .Authorize()
            .UseProjection();
        
        descriptor
            .Field(x => x.GetRepositories(null!))
            .Authorize()
            .UsePaging(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseProjection()
            .UseFiltering()
            .UseSorting();
        
        descriptor
            .Field(x => x.GetRepository(Guid.Empty, null!, CancellationToken.None))
            .Type<RepositoryType>()
            .Authorize()
            .UseProjection();
        
        descriptor
            .Field(x => x.GetRepositoryByName(string.Empty, null!, CancellationToken.None))
            .Type<RepositoryType>()
            .Authorize()
            .UseProjection();
        
        descriptor
            .Field(x => x.GetRelease(Guid.Empty, null!, CancellationToken.None))
            .Type<ReleaseType>()
            .Authorize()
            .UseProjection();
        
        descriptor
            .Field(x => x.GetChunkStores(null!, CancellationToken.None))
            .Authorize()
            .UsePaging(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseProjection()
            .UseFiltering()
            .UseSorting();
        
        descriptor
            .Field(x => x.GetChunkStore(Guid.Empty, null!, CancellationToken.None))
            .Type<ChunkStoreType>()
            .Authorize()
            .UseProjection();
        
        descriptor
            .Field(x => x.GetServiceAccounts(null!, CancellationToken.None))
            .Authorize()
            .UsePaging(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseProjection()
            .UseFiltering()
            .UseSorting();
        
        descriptor
            .Field(x => x.GetUsers(null!, CancellationToken.None))
            .Authorize()
            .UsePaging(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseProjection()
            .UseFiltering()
            .UseSorting();

        descriptor
            .Field(x => x.GetBackgroundJobs(null!, CancellationToken.None, null, null))
            .Authorize()
            .UsePaging(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseSorting();

        descriptor
            .Field(x => x.GetBackgroundJob(Guid.Empty, null!, CancellationToken.None))
            .Type<ObjectType<BackgroundJobGql>>()
            .Authorize();
    }
}