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

using BinStash.Server.GraphQL.Features.ChunkStores;
using BinStash.Server.GraphQL.Features.Instance;
using BinStash.Server.GraphQL.Features.Jobs;
using BinStash.Server.GraphQL.Features.Releases;
using BinStash.Server.GraphQL.Features.Repositories;
using BinStash.Server.GraphQL.Features.ServiceAccounts;
using BinStash.Server.GraphQL.Features.StorageClasses;
using BinStash.Server.GraphQL.Features.Tenants;
using BinStash.Server.GraphQL.Features.Users;

namespace BinStash.Server.GraphQL;

public class Query
{
    public async Task<TenantGql> GetCurrentTenant([Service] TenantQueryService service)
        => await service.GetCurrentTenantAsync();
    
    public async Task<IQueryable<TenantGql>> GetTenants([Service] TenantQueryService service)
        => await service.GetTenantsAsync();
    
    public async Task<TenantGql?> GetTenant(Guid id, [Service] TenantQueryService service, CancellationToken cancellationToken)
        => await service.GetTenantByIdAsync(id, cancellationToken);
    
    public async Task<IQueryable<RepositoryGql>> GetRepositories([Service] RepositoryQueryService service)
        => await service.GetRepositoriesAsync();
    
    public Task<RepositoryGql?> GetRepository(Guid id, [Service] RepositoryQueryService service, CancellationToken cancellationToken)
        => service.GetRepositoryByIdAsync(id, cancellationToken);
    
    public Task<RepositoryGql?> GetRepositoryByName(string name, [Service] RepositoryQueryService service, CancellationToken cancellationToken)
        => service.GetRepositoryByNameAsync(name, cancellationToken);
    
    public Task<ReleaseGql?> GetRelease(Guid id, [Service] ReleaseQueryService service, CancellationToken cancellationToken)
        => service.GetReleaseByIdAsync(id, cancellationToken);
    
    public Task<IQueryable<ChunkStoreGql>> GetChunkStores([Service] ChunkStoreQueryService service, CancellationToken cancellationToken)
        => service.GetChunkStoresAsync(cancellationToken);
    
    public Task<ChunkStoreGql?> GetChunkStore(Guid id, [Service] ChunkStoreQueryService service, CancellationToken cancellationToken)
        => service.GetChunkStoreByIdAsync(id, cancellationToken);
    
    public async Task<IQueryable<ServiceAccountGql>> GetServiceAccounts([Service] ServiceAccountQueryService service, CancellationToken cancellationToken)
        => await service.GetServiceAccountsAsync(cancellationToken);
    
    public async Task<IQueryable<UserGql>> GetUsers([Service] UserQueryService service, CancellationToken cancellationToken)
        => await service.GetUsersAsync(cancellationToken);
    
    public Task<IQueryable<BackgroundJobGql>> GetBackgroundJobs(
        [Service] BackgroundJobService service,
        CancellationToken cancellationToken,
        string? jobType = null,
        Guid? chunkStoreId = null)
        => service.GetBackgroundJobsAsync(jobType, chunkStoreId, cancellationToken);
    
    public Task<BackgroundJobGql?> GetBackgroundJob(Guid id, [Service] BackgroundJobService service, CancellationToken cancellationToken)
        => service.GetBackgroundJobAsync(id, cancellationToken);

    public Task<InstanceStatsGql> GetInstanceStats([Service] InstanceQueryService service, CancellationToken cancellationToken)
        => service.GetInstanceStatsAsync(cancellationToken);

    public Task<EmailConfigGql> GetEmailConfig([Service] InstanceQueryService service)
        => service.GetEmailConfigAsync();

    public Task<TenancyConfigGql> GetTenancyConfig([Service] InstanceQueryService service)
        => service.GetTenancyConfigAsync();

    public Task<DomainConfigGql> GetDomainConfig([Service] InstanceQueryService service)
        => service.GetDomainConfigAsync();

    public Task<List<StorageClassDetailsGql>> GetStorageClasses([Service] StorageClassQueryService service, CancellationToken cancellationToken)
        => service.GetStorageClassesAsync(cancellationToken);

    public Task<List<StorageClassDefaultMappingGql>> GetStorageClassDefaultMappings([Service] StorageClassQueryService service, CancellationToken cancellationToken)
        => service.GetStorageClassDefaultMappingsAsync(cancellationToken);

    public Task<List<ApiKeyInfoGql>> GetServiceAccountApiKeys(Guid serviceAccountId, [Service] ServiceAccountQueryService service, CancellationToken cancellationToken)
        => service.GetApiKeysAsync(serviceAccountId, cancellationToken);

    public Task<List<TenantMemberGql>> GetTenantMembers([Service] TenantQueryService service, CancellationToken cancellationToken)
        => service.GetTenantMembersAsync(cancellationToken);

    public Task<List<TenantStorageClassGql>> GetTenantStorageClasses([Service] TenantQueryService service, CancellationToken cancellationToken)
        => service.GetTenantStorageClassesAsync(cancellationToken);

    public Task<TenantInvitationPreviewGql?> GetTenantInvitationPreview(Guid tenantId, string code, [Service] TenantQueryService service, CancellationToken cancellationToken)
        => service.GetTenantInvitationPreviewAsync(tenantId, code, cancellationToken);

    public Task<ChunkStoreStatsGql?> GetChunkStoreStats(Guid chunkStoreId, [Service] ChunkStoreQueryService service, CancellationToken cancellationToken)
        => service.GetChunkStoreStatsAsync(chunkStoreId, cancellationToken);

    public Task<List<ChunkStoreTypeInfoGql>> GetEnabledChunkStoreTypes([Service] ChunkStoreQueryService service)
        => service.GetEnabledChunkStoreTypesAsync();
}