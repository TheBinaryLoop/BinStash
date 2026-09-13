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
using BinStash.Server.GraphQL.Features.Jobs;
using BinStash.Server.GraphQL.Features.Instance;
using BinStash.Server.GraphQL.Features.Repositories;
using BinStash.Server.GraphQL.Features.ServiceAccounts;
using BinStash.Server.GraphQL.Features.StorageClasses;
using BinStash.Server.GraphQL.Features.Tenants;

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
    
    public Task<ChunkStoreGql> CreateChunkStore(CreateChunkStoreInput input, [Service] ChunkStoreMutationService service, CancellationToken cancellationToken)
        => service.CreateChunkStoreAsync(input, cancellationToken);
    
    public Task<BackgroundJobGql> RebuildChunkStore(Guid chunkStoreId, [Service] ChunkStoreMutationService service, CancellationToken cancellationToken)
        => service.RebuildChunkStoreAsync(chunkStoreId, cancellationToken);
    
    public Task<BackgroundJobGql> UpgradeChunkStore(Guid chunkStoreId, [Service] ChunkStoreMutationService service, CancellationToken cancellationToken)
        => service.UpgradeChunkStoreAsync(chunkStoreId, cancellationToken);
    public Task<BackgroundJobGql> CancelBackgroundJob(Guid jobId, [Service] BackgroundJobService service, CancellationToken cancellationToken)
        => service.CancelBackgroundJobAsync(jobId, cancellationToken);

    public Task<SendTestEmailResultGql> SendTestEmail(string recipientEmail, [Service] InstanceMutationService service, CancellationToken cancellationToken)
        => service.SendTestEmailAsync(recipientEmail, cancellationToken);

    public Task<EmailConfigGql> SetEmailConfig(SetEmailConfigInput input, [Service] InstanceMutationService service)
        => service.SetEmailConfigAsync(input);

    public Task<TenancyConfigGql> SetTenancyConfig(SetTenancyConfigInput input, [Service] InstanceMutationService service)
        => service.SetTenancyConfigAsync(input);

    public Task<DomainConfigGql> SetDomainConfig(SetDomainConfigInput input, [Service] InstanceMutationService service)
        => service.SetDomainConfigAsync(input);

    public Task<bool> SetStorageClassDefaultMappings(SetStorageClassDefaultMappingsInput input, [Service] StorageClassMutationService service, CancellationToken cancellationToken)
        => service.SetStorageClassDefaultMappingsAsync(input, cancellationToken);

    public Task<RepositoryAccessGql> GrantRepositoryAccess(Guid repoId, short subjectType, Guid subjectId, string role, [Service] RepositoryMutationService service, CancellationToken cancellationToken)
        => service.GrantRepositoryAccessAsync(repoId, subjectType, subjectId, role, cancellationToken);

    public Task<bool> RevokeRepositoryAccess(Guid repoId, short subjectType, Guid subjectId, [Service] RepositoryMutationService service, CancellationToken cancellationToken)
        => service.RevokeRepositoryAccessAsync(repoId, subjectType, subjectId, cancellationToken);

    public Task<CreateApiKeyResultGql> CreateServiceAccountApiKey(Guid serviceAccountId, CreateServiceAccountApiKeyInput input, [Service] ServiceAccountMutationService service, CancellationToken cancellationToken)
        => service.CreateApiKeyAsync(serviceAccountId, input, cancellationToken);

    public Task<bool> DeleteServiceAccountApiKey(Guid serviceAccountId, Guid apiKeyId, [Service] ServiceAccountMutationService service, CancellationToken cancellationToken)
        => service.DeleteApiKeyAsync(serviceAccountId, apiKeyId, cancellationToken);

    public Task<bool> InviteTenantMember(InviteTenantMemberInput input, [Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.InviteTenantMemberAsync(input, cancellationToken);

    public Task<TenantMemberGql> UpdateTenantMemberRoles(Guid memberId, List<string> roles, [Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.UpdateTenantMemberRolesAsync(memberId, roles, cancellationToken);

    public Task<bool> RemoveTenantMember(Guid memberId, [Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.RemoveTenantMemberAsync(memberId, cancellationToken);

    public Task<bool> LeaveTenant([Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.LeaveTenantAsync(cancellationToken);

    public Task<bool> AcceptTenantInvitation(Guid tenantId, string code, [Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.AcceptTenantInvitationAsync(tenantId, code, cancellationToken);

    public Task<bool> DeleteTenant(Guid tenantId, [Service] TenantMutationService service, CancellationToken cancellationToken)
        => service.DeleteTenantAsync(tenantId, cancellationToken);
}