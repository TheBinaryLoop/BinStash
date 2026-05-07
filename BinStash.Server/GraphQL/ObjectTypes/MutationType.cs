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

namespace BinStash.Server.GraphQL.ObjectTypes;

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
    }
}