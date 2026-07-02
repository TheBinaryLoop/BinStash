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

using BinStash.Server.GraphQL.Features.Instance;
using BinStash.Server.GraphQL.Features.Repositories;
using BinStash.Server.GraphQL.Features.ServiceAccounts;
using BinStash.Server.GraphQL.Features.Tenants;

namespace BinStash.Server.GraphQL;

public sealed class MutationType : ObjectType<Mutation>
{
    protected override void Configure(IObjectTypeDescriptor<Mutation> descriptor)
    {
        descriptor
            .Field(x => x.CreateTenant(null!, null!, CancellationToken.None))
            .Type<TenantType>()
            .Authorize();
        
        descriptor
            .Field(x => x.UpdateTenant(null!, null!, CancellationToken.None))
            .Type<TenantType>()
            .Authorize();
        
        descriptor
            .Field(x => x.CreateRepository(null!, null!, CancellationToken.None))
            .Type<RepositoryType>()
            .Authorize();
        
        descriptor
            .Field(x => x.UpdateRepository(null!, null!, CancellationToken.None))
            .Type<RepositoryType>()
            .Authorize();
        
        /*descriptor
            .Field(x => x.DeleteRepository(null!, null!, CancellationToken.None))
            .Type<BooleanType>()
            .Authorize();*/
        
        descriptor
            .Field(x => x.CreateServiceAccount(null!, null!, CancellationToken.None))
            .Type<ServiceAccountType>()
            .Authorize();
        
        descriptor
            .Field(x => x.UpdateServiceAccount(null!, null!, CancellationToken.None))
            .Type<ServiceAccountType>()
            .Authorize();

        descriptor
            .Field(x => x.DeleteServiceAccount(Guid.Empty, null!, CancellationToken.None))
            .Type<BooleanType>()
            .Authorize();
        
        descriptor
            .Field(x => x.CreateChunkStore(null!, null!, CancellationToken.None))
            .Type<ObjectType<ChunkStoreGql>>()
            .Authorize();
        
        descriptor
            .Field(x => x.RebuildChunkStore(Guid.Empty, null!, CancellationToken.None))
            .Type<ObjectType<BackgroundJobGql>>()
            .Authorize();
        
        descriptor
            .Field(x => x.UpgradeChunkStore(Guid.Empty, null!, CancellationToken.None))
            .Type<ObjectType<BackgroundJobGql>>()
            .Authorize();

        descriptor
            .Field(x => x.CancelBackgroundJob(Guid.Empty, null!, CancellationToken.None))
            .Type<ObjectType<BackgroundJobGql>>()
            .Authorize();

        descriptor
            .Field(x => x.SendTestEmail(null!, null!, CancellationToken.None))
            .Type<ObjectType<SendTestEmailResultGql>>()
            .Authorize();

        descriptor
            .Field(x => x.SetEmailConfig(null!, null!))
            .Type<ObjectType<EmailConfigGql>>()
            .Authorize();

        descriptor
            .Field(x => x.SetTenancyConfig(null!, null!))
            .Type<ObjectType<TenancyConfigGql>>()
            .Authorize();

        descriptor
            .Field(x => x.SetDomainConfig(null!, null!))
            .Type<ObjectType<DomainConfigGql>>()
            .Authorize();

        descriptor
            .Field(x => x.SetStorageClassDefaultMappings(null!, null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.GrantRepositoryAccess(Guid.Empty, (short)0, Guid.Empty, null!, null!, CancellationToken.None))
            .Type<ObjectType<RepositoryAccessGql>>()
            .Authorize();

        descriptor
            .Field(x => x.RevokeRepositoryAccess(Guid.Empty, (short)0, Guid.Empty, null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.CreateServiceAccountApiKey(Guid.Empty, null!, null!, CancellationToken.None))
            .Type<ObjectType<CreateApiKeyResultGql>>()
            .Authorize();

        descriptor
            .Field(x => x.DeleteServiceAccountApiKey(Guid.Empty, Guid.Empty, null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.InviteTenantMember(null!, null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.UpdateTenantMemberRoles(Guid.Empty, null!, null!, CancellationToken.None))
            .Type<ObjectType<TenantMemberGql>>()
            .Authorize();

        descriptor
            .Field(x => x.RemoveTenantMember(Guid.Empty, null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.LeaveTenant(null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.AcceptTenantInvitation(Guid.Empty, null!, null!, CancellationToken.None))
            .Authorize();

        descriptor
            .Field(x => x.DeleteTenant(Guid.Empty, null!, CancellationToken.None))
            .Authorize();
    }
}