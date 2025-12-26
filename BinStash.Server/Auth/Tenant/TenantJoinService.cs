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

using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Configuration.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Auth.Tenant;

public class TenantJoinService(BinStashDbContext db, IOptions<TenancyOptions> opt)
{
    private readonly TenancyOptions _opt = opt.Value;

    public async Task JoinOnRegisterAsync(Guid userId, CancellationToken ct = default)
    {
        var tenantId = _opt.Mode == TenancyMode.Single
            ? _opt.SingleTenant.TenantId
            : throw new InvalidOperationException("Tenancy.Mode is Multi. Cannot auto-join tenant on register yet.");

        // Ensure membership exists (idempotent)
        var alreadyMember = await db.TenantMembers
            .AnyAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);

        if (!alreadyMember)
        {
            db.TenantMembers.Add(new TenantMember
            {
                TenantId = tenantId,
                UserId = userId,
                JoinedAt = DateTimeOffset.UtcNow
            });
        }
        
        // Make the first user admin if no admin exists yet
        var anyAdmin = await db.TenantRoleAssignments.AnyAsync(x => x.TenantId == tenantId && x.RoleName == "TenantAdmin", ct);

        if (!anyAdmin)
        {
            db.TenantRoleAssignments.Add(new TenantRoleAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                RoleName = "TenantAdmin",
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
    }
}