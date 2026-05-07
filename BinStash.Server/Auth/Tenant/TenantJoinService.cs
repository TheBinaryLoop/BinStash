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

using System.Text;
using BinStash.Contracts.Auth;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Configuration.Tenancy;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Auth.Tenant;

public class TenantJoinService(BinStashDbContext db, IOptionsMonitor<TenancySettings> opt)
{
    public async Task JoinOnRegisterAsync(Guid userId, RegisterRequest registerRequest, CancellationToken ct = default)
    {
        var tenancyOptions = opt.CurrentValue;
        var tenantId = Guid.Empty;
        var userIsAdmin = false;
        var userIsBillingAdmin = false;

        if (tenancyOptions.Mode == TenancyMode.Single)
        {
            tenantId = tenancyOptions.DefaultTenantId;
            
            // Make the first user admin if no admin exists yet
            var anyAdmin = await db.TenantRoleAssignments.AnyAsync(x => x.TenantId == tenantId && x.RoleName == "TenantAdmin", ct);
            
            userIsAdmin = !anyAdmin;
        }
        else
        {
            // For multi-tenancy, we will have multiple options here
            // 1. Automatically join a tenant via join rules, configured by TenantAdmin (e.g. email domain matches some domain)
            // 2. Join a tenant via an invite (e.g. sent by the tenant admin to the user's email)
            // 3. The user can create their own tenant and become admin of it (if allowed by the system)

            if (!string.IsNullOrEmpty(registerRequest.InvitationCode))
            {
                try
                {
                    var decodedBytes = WebEncoders.Base64UrlDecode(registerRequest.InvitationCode);
                    var decodedCode = Encoding.UTF8.GetString(decodedBytes);
                
                    var invitation = await db.TenantMemberInvitations
                        .Where(x => x.Code == decodedCode && x.ExpiresAt > DateTimeOffset.UtcNow && x.AcceptedAt == null)
                        .FirstOrDefaultAsync(ct);

                    if (invitation == null)
                    {
                        // Invalid or expired code, just ignore it and continue without joining a tenant
                        // The user can still join a tenant later via a valid code or by creating their own
                        return;
                    }
                
                    if (!string.Equals(invitation.InviteeEmail, registerRequest.Email, StringComparison.OrdinalIgnoreCase))
                    {
                        // The code is valid but was sent for a different email, ignore it to prevent abuse. Give the user feedback that the code is invalid, but don't reveal that the code exists for a different email.
                        // LOG: Warn about potential abuse with mismatching email and code
                        return;
                    }
                
                    tenantId = invitation.TenantId;
                
                    userIsAdmin = invitation.Roles.Contains("TenantAdmin");
                    userIsBillingAdmin = invitation.Roles.Contains("TenantBillingAdmin");
                
                    invitation.AcceptedAt = DateTimeOffset.UtcNow;
                }
                catch (Exception)
                {
                    // LOG: Warn about invalid code format with exception details for debugging
                    // Invalid code format, ignore it and continue without joining a tenant. The user can still join a tenant later via a valid code or by creating their own.
                }
            }
        }
        
        if (tenantId == Guid.Empty)
        {
            // No tenant to join, just return. The user can still join a tenant later by creating their own or via an invite.
            return;
        }
        

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
        
        
        if (userIsAdmin)
        {
            db.TenantRoleAssignments.Add(new TenantRoleAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                RoleName = "TenantAdmin",
                GrantedAt = DateTimeOffset.UtcNow
            });
        }
        
        if (userIsBillingAdmin)
        {
            db.TenantRoleAssignments.Add(new TenantRoleAssignment
            {
                TenantId = tenantId,
                UserId = userId,
                RoleName = "TenantBillingAdmin",
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
    }
}