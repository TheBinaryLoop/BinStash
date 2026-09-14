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
using BinStash.Server.GraphQL.Features.Audit;
using BinStash.Server.GraphQL.Features.ChunkStores;
using BinStash.Server.GraphQL.Features.Releases;
using BinStash.Server.GraphQL.Features.Repositories;
using BinStash.Server.GraphQL.Features.ServiceAccounts;
using BinStash.Server.GraphQL.Features.Tenants;
using BinStash.Server.GraphQL.Features.Usage;

namespace BinStash.Server.GraphQL;

public sealed class QueryType : ObjectType<Query>
{
    protected override void Configure(IObjectTypeDescriptor<Query> descriptor)
    {
        // Fail closed: without this, HotChocolate implicitly binds every public method on Query to a
        // schema field, so a new resolver would be exposed before anyone declared its authorization.
        // With explicit binding a field only exists once it is declared here, next to its guard.
        descriptor.BindFieldsExplicitly();

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
            .Field(x => x.GetRepositoryMoveTargets(Guid.Empty, null!, CancellationToken.None))
            .Type<ListType<NonNullType<ObjectType<RepositoryMoveTargetGql>>>>()
            .Authorize();
        
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

        descriptor
            .Field(x => x.GetInstanceStats(null!, CancellationToken.None))
            .Type<ObjectType<InstanceStatsGql>>()
            .Authorize();

        descriptor
            .Field(x => x.GetEmailConfig(null!))
            .Type<ObjectType<EmailConfigGql>>()
            .Authorize();

        descriptor
            .Field(x => x.GetTenancyConfig(null!))
            .Type<ObjectType<TenancyConfigGql>>()
            .Authorize();

        descriptor
            .Field(x => x.GetDomainConfig(null!))
            .Type<ObjectType<DomainConfigGql>>()
            .Authorize();

        descriptor
            .Field(x => x.GetGcConfig(null!))
            .Type<NonNullType<ObjectType<GcConfigGql>>>()
            .Authorize();

        descriptor
            .Field(x => x.GetStorageClasses(null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.GetStorageClassDefaultMappings(null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.GetServiceAccountApiKeys(Guid.Empty, null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.GetTenantMembers(null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.GetTenantStorageClasses(null!, CancellationToken.None))
            .Authorize();

        // Invitation preview is intentionally public (unauthenticated onboarding flow).
        descriptor
            .Field(x => x.GetTenantInvitationPreview(Guid.Empty, null!, null!, CancellationToken.None))
            .Type<ObjectType<TenantInvitationPreviewGql>>();

        descriptor
            .Field(x => x.GetChunkStoreStats(Guid.Empty, null!, CancellationToken.None))
            .Type<ObjectType<ChunkStoreStatsGql>>()
            .Authorize();

        descriptor
            .Field(x => x.GetChunkStoreStatsHistory(Guid.Empty, null!, CancellationToken.None, 30))
            .Type<NonNullType<ListType<NonNullType<ObjectType<ChunkStoreStatsGql>>>>>()
            .Authorize();

        descriptor
            .Field(x => x.GetEnabledChunkStoreTypes(null!))
            .Authorize();

        descriptor
            .Field(x => x.GetTenantUsage(null!, CancellationToken.None))
            .Type<NonNullType<ObjectType<TenantUsageGql>>>()
            .Authorize();

        // Audit trails are paged/filtered/sorted server-side: these tables grow without bound and
        // the UI only ever shows a window of them.
        descriptor
            .Field(x => x.GetAuditLog(null!))
            .Authorize()
            .UsePaging<ObjectType<AuditLogEntryGql>>(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseFiltering()
            .UseSorting();

        descriptor
            .Field(x => x.GetInstanceAuditLog(null!))
            .Authorize()
            .UsePaging<ObjectType<AuditLogEntryGql>>(options: new PagingOptions
            {
                IncludeTotalCount = true
            })
            .UseFiltering()
            .UseSorting();
    }
}