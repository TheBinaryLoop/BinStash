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

using System.Security.Claims;
using BinStash.Contracts.Tenant;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Auth;
using BinStash.Server.Context;
using BinStash.Server.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class TenantEndpoints
{
    public static RouteGroupBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants")
            .WithTags("Tenant")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthDefaults.AuthenticationScheme });
        
        var explicitTenantGroup = group.MapGroup("/{tenantId:guid}");
        
        group.MapGet("/", ListTenantsForMember)   
            .WithDescription("Get tenants the user is a member of.")
            .WithSummary("List Tenants");
        
        group.MapGet("/{id:guid}", GetTenant)
            .WithDescription("Get a tenant by ID.")
            .WithSummary("Get Tenant");
        
        group.MapGet("/current", GetCurrentTenant)
            .WithDescription("Get the current tenant.")
            .WithSummary("Get Current Tenant")
            .RequireTenantPermission(TenantPermission.Member);
        
        group.MapGet("/current/members", GetMembersForTenant)
            .WithDescription("Get members of the current tenant.")
            .WithSummary("Get Tenant Members")
            .RequireTenantPermission(TenantPermission.Admin);
        explicitTenantGroup.MapGet("/members", GetMembersForTenant)
            .WithDescription("Get members of a tenant.")
            .WithSummary("Get Tenant Members")
            .RequireTenantPermission(TenantPermission.Admin);
        
        // POST /api/tenants/{tenantId}/invitations
        // - Permission: TenantAdmin
        // - Why: invite flow is the typical SaaS onboarding mechanism; avoids public registration.
        // - When: admin invites a coworker by email.
        
        // POST /api/tenants/{tenantId}/invitations/{code}/accept
        // - Permission: Public or Authenticated - Evaluate which fits better
        // - Why: accepts invite and creates membership.
        // - When: user clicks the link from the email; CLI can accept with code.
        // Returns: membership summary
        // { "tenantId": "...", "userId": "...", "roles": ["Member"] }
        
        group.MapDelete("/current/members/{memberId:guid}", RemoveMemberFromTenant)
            .WithDescription("Remove a member from the current tenant.")
            .WithSummary("Remove Tenant Member")
            .RequireTenantPermission(TenantPermission.Admin);
        explicitTenantGroup.MapDelete("/members/{memberId:guid}", RemoveMemberFromTenant)
            .WithDescription("Remove a member from a tenant.")
            .WithSummary("Remove Tenant Member")
            .RequireTenantPermission(TenantPermission.Admin);
        
        group.MapPost("/current/leave", LeaveTenant)
            .WithDescription("Leave the current tenant.")
            .WithSummary("Leave Tenant")
            .RequireTenantPermission(TenantPermission.Member);
        explicitTenantGroup.MapPost("/leave", LeaveTenant)
            .WithDescription("Leave a tenant.")
            .WithSummary("Leave Tenant")
            .RequireTenantPermission(TenantPermission.Member);
        
        group.MapPatch("/current/members/{memberId:guid}", UpdateMemberRoles)
            .WithDescription("Update roles for a tenant member.")
            .WithSummary("Update Tenant Member Roles")
            .RequireTenantPermission(TenantPermission.Admin);
        explicitTenantGroup.MapPatch("/members/{memberId:guid}", UpdateMemberRoles)
            .WithDescription("Update roles for a tenant member.")
            .WithSummary("Update Tenant Member Roles")
            .RequireTenantPermission(TenantPermission.Admin);
        
        return group;
    }

    private static async Task<IResult> GetMembersForTenant(HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {        
        var members = await db.TenantMembers.AsNoTracking()
            .Join(db.Users, tm => tm.UserId, u => u.Id, (tm, u) => new { tm, u })
            .Where(x => x.tm.TenantId == tenantContext.TenantId)
            .Select(x => new
            {
                User = new
                {
                    x.u.Id,
                    x.u.Email
                },
                x.tm.JoinedAt
            })
            .ToListAsync();
        
        var memberRoles = await db.TenantRoleAssignments.AsNoTracking().Where(t => t.TenantId == tenantContext.TenantId).ToListAsync();
        
        var membersWithRoles = members.Select(m => new
        {
            m.User.Id,
            m.User.Email,
            m.JoinedAt,
            Roles = memberRoles.Where(r => r.UserId == m.User.Id).Select(r => r.RoleName).ToList()
        });
        
        return Results.Ok(membersWithRoles);
    }

    private static async Task<IResult> ListTenantsForMember(HttpContext context, BinStashDbContext db)
    {
        var userId = await db.Users.FirstOrDefaultAsync(u => u.Email == context.User.Identity!.Name);
        if (userId == null)
            return Results.Unauthorized();
        
        var tenants = await db.TenantMembers.AsNoTracking().Include(tm => tm.Tenant).Where(tm => tm.UserId == userId.Id).Select(tm => new
        {
            tm.Tenant.Id,
            tm.Tenant.Name,
            tm.Tenant.Slug,
            tm.JoinedAt
        }).ToListAsync();
        
        return Results.Ok(tenants);
    }
    
    private static async Task<IResult> GetTenant(Guid id, HttpContext context, BinStashDbContext db)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return Results.Unauthorized();
        
        var isMember = await db.TenantMembers
            .AnyAsync(m => m.TenantId == id && m.UserId == userId);

        if (!isMember)
            return Results.NotFound();
        
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Slug,
                t.CreatedAt
            })
            .FirstOrDefaultAsync()
            .ContinueWith<IResult>(t =>
            {
                var tenant = t.Result;
                if (tenant == null)
                    return Results.NotFound("Tenant not found.");
                
                return Results.Ok(tenant);
            });
    }
    
    private static async Task<IResult> GetCurrentTenant(HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Slug,
                t.CreatedAt
            })
            .FirstOrDefaultAsync();
        
        if (tenant == null)
            return Results.NotFound("Tenant not found.");
        
        return Results.Ok(tenant);
    }
    
    private static async Task<IResult> RemoveMemberFromTenant(Guid memberId, HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        // Safeguard: prevent removing oneself
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return Results.BadRequest();
        
        var membership = await db.TenantMembers
            .FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == memberId && tm.UserId != userId);
        
        if (membership == null)
            return Results.NotFound("Member not found in tenant.");
        
        var roleAssignments = await db.TenantRoleAssignments
            .Where(r => r.TenantId == tenantContext.TenantId && r.UserId == memberId && r.UserId != userId)
            .ToListAsync();
        
        db.TenantRoleAssignments.RemoveRange(roleAssignments);
        db.TenantMembers.Remove(membership);
        await db.SaveChangesAsync();
        
        return Results.NoContent();
    }
    
    private static async Task<IResult> LeaveTenant(HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return Results.BadRequest();
        
        var membership = await db.TenantMembers
            .FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == userId);
        
        if (membership == null)
            return Results.NotFound("You are not a member of this tenant.");
        
        // Safeguard: prevent leave if last admin
        var tenantAdmins = await db.TenantRoleAssignments.Where(r => r.TenantId == tenantContext.TenantId && r.RoleName == "TenantAdmin").ToListAsync();
        if (tenantAdmins.Count == 1 && tenantAdmins[0].UserId == userId)
            return Results.BadRequest("Cannot leave tenant; last admin.");
        
        db.TenantMembers.Remove(membership);
        await db.SaveChangesAsync();
        
        return Results.NoContent();
    }
    
    private static async Task<IResult> UpdateMemberRoles(Guid memberId, UpdateTenantMemberRolesDto request, HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        if (request?.Roles == null)
            return Results.BadRequest("Roles are required.");
    
        var membership = await db.TenantMembers
            .FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == memberId);
        
        if (membership == null)
            return Results.NotFound("Member not found in tenant.");
        
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var currentUserId))
            return Results.BadRequest();
    
        var existingRoles = await db.TenantRoleAssignments
            .Where(r => r.TenantId == tenantContext.TenantId && r.UserId == memberId)
            .ToListAsync();
        
        // Safeguard: prevent demoting oneself (cannot remove any of own existing roles) // TODO: refine logic to only protect admin role
        if (memberId == currentUserId)
        {
            var existingRoleNames = existingRoles.Select(r => r.RoleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requestedRoleNames = request.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
    
            if (!requestedRoleNames.IsSupersetOf(existingRoleNames))
                return Results.BadRequest("Cannot demote yourself.");
        }
        
        // Remove roles not in the new set
        foreach (var existingRole in existingRoles)
            if (!request.Roles.Contains(existingRole.RoleName))
                db.TenantRoleAssignments.Remove(existingRole);
        
        var newRoles = request.Roles.Where(r => existingRoles.All(x => x.RoleName != r)).Select(roleName => new TenantRoleAssignment
        {
            TenantId = tenantContext.TenantId,
            UserId = memberId,
            RoleName = roleName
        });
        
        await db.TenantRoleAssignments.AddRangeAsync(newRoles);
        await db.SaveChangesAsync();
        
        return Results.Ok(new TenantMemberDto(tenantContext.TenantId, memberId, request.Roles));
    }
}