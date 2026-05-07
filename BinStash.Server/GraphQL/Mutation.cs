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

using BinStash.Server.GraphQL.Inputs;
using BinStash.Server.GraphQL.Services;

namespace BinStash.Server.GraphQL;

public sealed class Mutation
{
    public Task<TenantGql> CreateTenant(CreateTenantInput input, [Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.CreateTenantAsync(input, cancellationToken);
    
    public Task<TenantGql> UpdateTenant(UpdateTenantInput input, [Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.UpdateTenantAsync(input, cancellationToken);
    
    public Task<RepositoryGql> CreateRepository(CreateRepositoryInput input, [Service] RepositoryMutationService service, CancellationToken cancellationToken)
        => service.CreateRepositoryAsync(input, cancellationToken);
    
    public Task<RepositoryGql> UpdateRepository(UpdateRepositoryInput input, [Service] RepositoryMutationService service, CancellationToken cancellationToken)
        => service.UpdateRepositoryAsync(input, cancellationToken);
    
    /*public Task DeleteRepository(Guid repoId, [Service] RepositoryMutationService service, CancellationToken cancellationToken)
        => service.DeleteRepositoryAsync(repoId, cancellationToken);*/
    
    public Task<ServiceAccountGql> CreateServiceAccount(CreateServiceAccountInput input, [Service] ServiceAccountMutationService service, CancellationToken cancellationToken)
        => service.CreateServiceAccountAsync(input, cancellationToken);
    
    public Task<ServiceAccountGql> UpdateServiceAccount(UpdateServiceAccountInput input, [Service] ServiceAccountMutationService service, CancellationToken cancellationToken)
        => service.UpdateServiceAccountAsync(input, cancellationToken);
    
    public Task DeleteServiceAccount(Guid accountId, [Service] ServiceAccountMutationService service, CancellationToken cancellationToken)
        => service.DeleteServiceAccountAsync(accountId, cancellationToken);
}