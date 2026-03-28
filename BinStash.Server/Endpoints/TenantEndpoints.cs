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
using System.Text;
using BinStash.Contracts.Tenant;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Context;
using BinStash.Server.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class TenantEndpoints
{ 
    // We'll figure out a unique endpoint name based on the final route pattern during endpoint generation.
    private static string? _acceptInvitationEndpointName;
    
    public static RouteGroupBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants")!
            .WithTags("Tenant")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();
        
        // TODO: Create tenant endpoint (auth only, no tenant context yet)
        
        var explicitTenantGroup = group.MapGroup("/{tenantId:guid}")!;
        
        group.MapGet("/", ListTenantsForMember)!
            .WithDescription("Get tenants the user is a member of.")
            .WithSummary("List Tenants");
        group.MapPost("/", CreateTenant)!
            .WithDescription("Create a new tenant.")
            .WithSummary("Create Tenant")
            .RequireInstancePermission(InstancePermission.Admin);
        
        group.MapGet("/{id:guid}", GetTenant)!
            .WithDescription("Get a tenant by ID.")
            .WithSummary("Get Tenant");
        
        explicitTenantGroup.MapPut("/", UpdateTenant)!
            .WithDescription("Update tenant details.")
            .WithSummary("Update Tenant")
            .RequireTenantPermission(TenantPermission.Admin);
        
        explicitTenantGroup.MapDelete("/", (HttpContext context) => Results.StatusCode(StatusCodes.Status501NotImplemented))! // TODO: implement tenant deletion with safeguards (e.g. only if no members, or transfer ownership first)
            .WithDescription("Delete a tenant. (Not implemented yet)")
            .WithSummary("Delete Tenant")
            .RequireTenantPermission(TenantPermission.Admin);
        
        group.MapGet("/current", GetCurrentTenant)!
            .WithDescription("Get the current tenant.")
            .WithSummary("Get Current Tenant")
            .RequireTenantPermission(TenantPermission.Member);
        
        group.MapGet("/current/members", GetMembersForTenant)!
            .WithDescription("Get members of the current tenant.")
            .WithSummary("Get Tenant Members")
            .RequireTenantPermission(TenantPermission.Admin);
        explicitTenantGroup.MapGet("/members", GetMembersForTenant)!
            .WithDescription("Get members of a tenant.")
            .WithSummary("Get Tenant Members")
            .RequireTenantPermission(TenantPermission.Admin);
        
        // POST /api/tenants/{tenantId}/invitations
                // - Permission: TenantAdmin
                // - Why: invite flow is the typical SaaS onboarding mechanism; avoids public registration.
                // - When: admin invites a coworker by email.
        group.MapPost("/current/invatations", InviteMemberAsync)!
            .WithDescription("Invite a member to a tenant.")
            .WithSummary("Invite Tenant Member")
            .RequireTenantPermission(TenantPermission.Admin);
        explicitTenantGroup.MapPost("/invitations", InviteMemberAsync)!
            .WithDescription("Invite a member to a tenant.")
            .WithSummary("Invite Tenant Member")
            .RequireTenantPermission(TenantPermission.Admin);
        
        app.MapGet("/api/tenants/{tenantId}/invitations/{code}/preview", GetInvitationPreviewAsync)!
            .WithTags("Tenant")
            .WithDescription("Get a preview of a tenant invitation.")
            .WithSummary("Get Tenant Invitation Preview")
            .AllowAnonymous();
        
        
        // GET /api/tenants/{tenantId}/invitations/{code}/accept
        // - Permission: Public or Authenticated - Evaluate which fits better
        // - Why: accepts invite and creates membership or routes to registration if user not found.
        // - When: user clicks the link from the email; CLI can accept with code.
        // Returns: membership summary
        // { "tenantId": "...", "userId": "...", "roles": ["Member"] }
        
        group.MapGet("/current/invitations/{code}/accept", AcceptInvitationAsync)!
            .WithDescription("Accept an invitation to join the current tenant.")
            .WithSummary("Accept Tenant Invitation")
            .AllowAnonymous()
            .Add(endpointBuilder =>
            {
                var finalPattern = ((RouteEndpointBuilder)endpointBuilder).RoutePattern.RawText;
                _acceptInvitationEndpointName = $"{nameof(MapTenantEndpoints)}-{finalPattern}";
                endpointBuilder.Metadata.Add(new EndpointNameMetadata(_acceptInvitationEndpointName));
            });;
        
        // POST /api/tenants/{tenantId}/invitations/{code}/accept
        // - Permission: Public or Authenticated - Evaluate which fits better
        // - Why: accepts invite and creates membership.
        // - When: user clicks the link from the email; CLI can accept with code.
        // Returns: membership summary
        // { "tenantId": "...", "userId": "...", "roles": ["Member"] }
        
        group.MapDelete("/current/members/{memberId:guid}", RemoveMemberFromTenant)!
            .WithDescription("Remove a member from the current tenant.")
            .WithSummary("Remove Tenant Member")
            .RequireTenantPermission(TenantPermission.Admin);
        explicitTenantGroup.MapDelete("/members/{memberId:guid}", RemoveMemberFromTenant)!
            .WithDescription("Remove a member from a tenant.")
            .WithSummary("Remove Tenant Member")
            .RequireTenantPermission(TenantPermission.Admin);
        
        group.MapPost("/current/leave", LeaveTenant)!
            .WithDescription("Leave the current tenant.")
            .WithSummary("Leave Tenant")
            .RequireTenantPermission(TenantPermission.Member);
        explicitTenantGroup.MapPost("/leave", LeaveTenant)!
            .WithDescription("Leave a tenant.")
            .WithSummary("Leave Tenant")
            .RequireTenantPermission(TenantPermission.Member);
        
        group.MapPatch("/current/members/{memberId:guid}", UpdateMemberRoles)!
            .WithDescription("Update roles for a tenant member.")
            .WithSummary("Update Tenant Member Roles")
            .RequireTenantPermission(TenantPermission.Admin);
        explicitTenantGroup.MapPatch("/members/{memberId:guid}", UpdateMemberRoles)!
            .WithDescription("Update roles for a tenant member.")
            .WithSummary("Update Tenant Member Roles")
            .RequireTenantPermission(TenantPermission.Admin);
        
        // TODO: Move to a more fitting location
        group.MapGet("/current/storage-classes", GetStorageClassesForTenant)!
            .WithDescription("Get storage classes for the current tenant.")
            .WithSummary("Get Tenant Storage Classes")
            .RequireTenantPermission(TenantPermission.Member);
        explicitTenantGroup.MapGet("/storage-classes", GetStorageClassesForTenant)!
            .WithDescription("Get storage classes for a tenant.")
            .WithSummary("Get Tenant Storage Classes")
            .RequireTenantPermission(TenantPermission.Member);
        
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

    private static async Task<IResult> CreateTenant(CreateTenantDto request, HttpContext context, BinStashDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            return Results.BadRequest("Name and Slug are required.");

        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return Results.Unauthorized();
        
        var existingSlug = await db.Tenants.AnyAsync(t => t.Slug.ToLower() == request.Slug.ToLower());
        if (existingSlug)
            return Results.BadRequest("Slug already exists.");
        
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name,
            Slug = request.Slug.ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId
        };
        
        await db.Tenants.AddAsync(tenant);
        
        await db.SaveChangesAsync();
        
        return Results.Created($"/api/tenants/{tenant.Id}", new { tenant.Id, tenant.Name, tenant.Slug, tenant.CreatedAt });
    }
    
    private static async Task<IResult> UpdateTenant(UpdateTenantDto request, HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        var tenant = await db.Tenants.FindAsync(tenantContext.TenantId);
        if (tenant == null)
            return Results.NotFound("Tenant not found.");
        
        if (!string.IsNullOrWhiteSpace(request.Name))
            tenant.Name = request.Name;
        
        if (!string.IsNullOrWhiteSpace(request.Slug) && !request.Slug.Equals(tenant.Slug, StringComparison.CurrentCultureIgnoreCase))
        {
            var existingSlug = await db.Tenants.AnyAsync(t => t.Slug.Equals(request.Slug, StringComparison.CurrentCultureIgnoreCase) && t.Id != tenant.Id);
            if (existingSlug)
                return Results.BadRequest("Slug already exists.");
            
            tenant.Slug = request.Slug.ToLowerInvariant();
        }
        
        await db.SaveChangesAsync();
        
        return Results.Ok(new { tenant.Id, tenant.Name, tenant.Slug, tenant.CreatedAt });
    }
    
    private static async Task<IResult> ListTenantsForMember(HttpContext context, BinStashDbContext db)
    {
        var userId = await db.Users.FirstOrDefaultAsync(u => u.Email == context.User.Identity!.Name);
        if (userId == null)
            return Results.Unauthorized();
        
        // Check if the user is an instance admin and return all tenants if so (instance admins are implicitly tenant members of all tenants)
        if (await db.UserRoles.Where(ur => ur.UserId == userId.Id)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r)
                .AnyAsync(r => r.Name == "InstanceAdmin"))
        {
            return await db.Tenants.AsNoTracking()
                .Select(t => new TenantInfoDto
                (
                    t.Id,
                    t.Name,
                    t.Slug,
                    DateTimeOffset.UtcNow, // joined date is not applicable for instance admins
                    "TenantAdmin" // role is implicitly TenantAdmin for all tenants
                ))
                .ToListAsync()
                .ContinueWith<IResult>(t =>
                {
                    var tenants = t.Result;
                    return Results.Ok(tenants);
                });
        }
        
        // Get a list of all tenants the user is a member of, including tenant info and joined date as well as the role of the user in the tenant (e.g. admin, member, etc.)
        var tenants = await db.TenantMembers.AsNoTracking()
            .Where(tm => tm.UserId == userId.Id)
            .Join(db.Tenants.AsNoTracking(), tm => tm.TenantId, t => t.Id, (tm, t) => new TenantInfoDto
            (
                t.Id,
                t.Name,
                t.Slug,
                tm.JoinedAt,
                db.TenantRoleAssignments.Where(r => r.TenantId == tm.TenantId && r.UserId == tm.UserId).OrderBy(r => r.RoleName).Select(r => r.RoleName).First()
            ))
            .ToListAsync();
        
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
    
    private static async Task<IResult> InviteMemberAsync(InviteTenantMemberDto request, HttpContext context, BinStashDbContext db, TenantContext tenantContext, UserManager<BinStashUser> userManager, ITenantEmailSender emailSender, LinkGenerator linkGenerator)
    {
        var tenant = await db.Tenants.FindAsync(tenantContext.TenantId);
        if (tenant == null)
            return Results.NotFound("Tenant not found.");
        
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return Results.BadRequest();
        
        var inviter = await userManager.FindByIdAsync(userId.ToString());
        if (inviter == null)
            return Results.NotFound("Inviter user not found.");
        
        var existingUser = await userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
        {
            var existingMember = await db.TenantMembers.FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == existingUser.Id);
            if (existingMember != null)
                return Results.BadRequest("Cannot invite member; already a member.");
        }
        // Create invitation
        var invitation = new TenantMemberInvitation
        {
            TenantId = tenantContext.TenantId,
            InviterId = userId,
            InviteeEmail = request.Email,
            Roles = request.Roles,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Code = Guid.NewGuid().ToString("N")
        };
        
        await db.TenantMemberInvitations.AddAsync(invitation);
        await db.SaveChangesAsync();
        
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(invitation.Code));
        
        // TODO: Get host form settings
        var acceptInvitationFrontendUrl = $"http://localhost:5173/invite/{tenantContext.TenantId:D}/{code}";
        var acceptEmailUrl = !string.IsNullOrEmpty(_acceptInvitationEndpointName) ? linkGenerator.GetUriByName(context, _acceptInvitationEndpointName, values: new { code })
                              : throw new NotSupportedException($"Could not find endpoint named '{_acceptInvitationEndpointName}'.");
        
        await emailSender.SendMemberInvitationEmailAsync(inviter, tenant, request.Email, acceptInvitationFrontendUrl);
        
        return Results.Json(new {});
    }
    
    private static async Task<IResult> GetInvitationPreviewAsync(string code, HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        if (string.IsNullOrEmpty(code))
            return Results.BadRequest("Invitation code is required.");

        try
        {
            var decodedBytes = WebEncoders.Base64UrlDecode(code);
            var decodedCode = Encoding.UTF8.GetString(decodedBytes);
            var invitation = await db.TenantMemberInvitations.FirstOrDefaultAsync(i => i.TenantId == tenantContext.TenantId && i.Code == decodedCode && i.ExpiresAt > DateTimeOffset.UtcNow && i.AcceptedAt == null);
            if (invitation == null)
                return Results.NotFound("Invitation not found or expired.");
        
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId);
            if (tenant == null)
                return Results.NotFound("Tenant not found.");
        
            return Results.Ok(new TenantInvitationPreviewDto(tenant.Id, tenant.Name, tenant.Slug, invitation.Roles.First(), invitation.InviteeEmail, invitation.ExpiresAt));
        }
        catch (Exception)
        {
            return Results.BadRequest("Invalid invitation code.");
        }
    }
    
    private static async Task<IResult> AcceptInvitationAsync(string code, HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        if (string.IsNullOrEmpty(code))
            return Results.BadRequest("Invitation code is required.");

        try
        {
            var decodedBytes = WebEncoders.Base64UrlDecode(code);
            var decodedCode = Encoding.UTF8.GetString(decodedBytes);
            var invitation = await db.TenantMemberInvitations.FirstOrDefaultAsync(i => i.TenantId == tenantContext.TenantId && i.Code == decodedCode && i.ExpiresAt > DateTimeOffset.UtcNow && i.AcceptedAt == null);
            if (invitation == null)
                return Results.NotFound("Invitation not found or expired.");
        
            // Check if the user is authenticated
            var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
            if (!Guid.TryParse(userIdStr, out var userId))
                return Results.Unauthorized(); // Redirect to registration/login flow
        
            var existingMember = await db.TenantMembers.FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == userId);
            if (existingMember != null)
                return Results.BadRequest("You are already a member of this tenant.");
        
            // Create membership
            var membership = new TenantMember
            {
                TenantId = tenantContext.TenantId,
                UserId = userId,
                JoinedAt = DateTimeOffset.UtcNow
            };
        
            await db.TenantMembers.AddAsync(membership);
            // Assign roles
            var roleAssignments = invitation.Roles.Select(roleName => new TenantRoleAssignment
            {  
                TenantId = tenantContext.TenantId,
                UserId = userId,
                RoleName = roleName,
                GrantedAt = DateTimeOffset.UtcNow
            });
        
            await db.TenantRoleAssignments.AddRangeAsync(roleAssignments);
        
            // Remove invitation
            invitation.AcceptedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(new TenantMemberDto(tenantContext.TenantId, userId, invitation.Roles));
        }
        catch (Exception)
        {
            return Results.BadRequest("Invalid invitation code.");
        }
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
            return Results.BadRequest("You are not a member of this tenant.");
        
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
    
    private static async Task<IResult> GetStorageClassesForTenant(HttpContext context, BinStashDbContext db, TenantContext tenantContext)
    {
        // join StorageClassMapping (tenantId, enabled) with StorageClass catalog.
        var storageClasses = await db.StorageClassMappings.AsNoTracking()
            .Where(scm => scm.TenantId == tenantContext.TenantId && scm.IsEnabled)
            .Join(db.StorageClasses.AsNoTracking(),
                scm => scm.StorageClassName,
                sc => sc.Name,
                (scm, sc) => new
                {
                    sc.Name,
                    sc.Description,
                    scm.IsDefault
                })
            .ToListAsync();
        return Results.Ok(storageClasses);
    }
}