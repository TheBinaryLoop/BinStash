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

using BinStash.Server.GraphQL.Services;

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
}