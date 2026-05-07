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

using System.Security.Claims;
using BinStash.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.ObjectTypes;

public class TenantType : ObjectType<TenantGql>
{
    protected override void Configure(IObjectTypeDescriptor<TenantGql> descriptor)
    {
        descriptor
            .Field(x => x.Id)
            .Type<NonNullType<UuidType>>();
        
        descriptor
            .Field(x => x.Name)
            .Type<NonNullType<StringType>>();
        
        descriptor
            .Field(x => x.Slug)
            .Type<NonNullType<StringType>>();
        
        descriptor
            .Field(x => x.CreatedAt)
            .Type<NonNullType<DateTimeType>>();
        
        descriptor
            .Field(x  => x.JoinedAt)
            .Type<DateTimeType>();
        
        descriptor.Field("myRoles")
            .Type<NonNullType<ListType<NonNullType<StringType>>>>()
            .ResolveWith<Resolvers>(x => x.GetMyRolesAsync(null!, null!, null!, CancellationToken.None));
    }
    
    private sealed class Resolvers
    {
        public async Task<IReadOnlyList<string>> GetMyRolesAsync([Parent] TenantGql tenant, [Service] BinStashDbContext db, IHttpContextAccessor httpContextAccessor, CancellationToken ct)
        {
            var user = httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

            if (!Guid.TryParse(userIdStr, out var userId))
                return Array.Empty<string>();

            var isInstanceAdmin = await db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
                .AnyAsync(r => r == "InstanceAdmin", ct);

            if (isInstanceAdmin)
                return ["TenantAdmin"];

            return await db.TenantRoleAssignments
                .AsNoTracking()
                .Where(r => r.TenantId == tenant.Id && r.UserId == userId)
                .OrderBy(r => r.RoleName)
                .Select(r => r.RoleName)
                .ToListAsync(ct);
        }
    }
}