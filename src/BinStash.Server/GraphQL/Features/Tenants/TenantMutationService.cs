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
using System.Text;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Configuration;
using BinStash.Server.Endpoints;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using BinStash.Core.Auditing;

namespace BinStash.Server.GraphQL.Features.Tenants;

public sealed class TenantMutationService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<BinStashUser> _userManager;
    private readonly ITenantEmailSender _emailSender;
    private readonly IOptions<DomainSettings> _domainOptions;
    private readonly IAuditLogWriter _audit;

    public TenantMutationService(
        BinStashDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService,
        UserManager<BinStashUser> userManager,
        ITenantEmailSender emailSender,
        IOptions<DomainSettings> domainOptions,
        IAuditLogWriter audit)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
        _userManager = userManager;
        _emailSender = emailSender;
        _domainOptions = domainOptions;
        _audit = audit;
    }

    private (ClaimsPrincipal User, Guid UserId) RequireUser()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            throw new GraphQLException("Invalid user context.");
        return (user, userId);
    }
    
    public async Task<TenantGql> CreateTenantAsync(CreateTenantInput input, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        if (!Guid.TryParse(userIdStr, out var userId))
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unauthorized.")
                    .SetCode("UNAUTHORIZED")
                    .Build());

        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        if (string.IsNullOrWhiteSpace(input.Name))
            throw new GraphQLException("Tenant name is required.");
        
        if (string.IsNullOrWhiteSpace(input.Slug))
            throw new GraphQLException("Tenant slugss is required.");
        
        if (await _db.Tenants.AnyAsync(x => x.Name == input.Name, ct))
        {
            throw new GraphQLException($"A tenant with the name '{input.Name}' already exists.");
        }
        
        if (await _db.Tenants.AnyAsync(x => x.Slug == input.Slug, ct))
        {
            throw new GraphQLException($"A tenant with the slug '{input.Slug}' already exists.");
        }
        
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Name = input.Name,
            Slug = input.Slug,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            Status = TenantStatus.Active
        };

        await _db.Tenants.AddAsync(tenant, ct);

        // Seed the tenant's storage-class mappings from the instance-wide defaults.
        // Without this a freshly created tenant has no storage classes at all, so
        // createRepository fails with "No default storage class is configured for this
        // tenant" and the workspace is unusable. The setup wizard does the same thing for
        // the tenant it creates (SetupEndpoints.EnsureStorageDefaultsAsync); every tenant
        // created afterwards needs it too.
        var defaultMappings = await _db.StorageClassDefaultMappings
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var mapping in defaultMappings)
        {
            await _db.StorageClassMappings.AddAsync(new StorageClassMapping
            {
                TenantId = tenant.Id,
                StorageClassName = mapping.StorageClassName,
                ChunkStoreId = mapping.ChunkStoreId,
                IsDefault = mapping.IsDefault,
                IsEnabled = mapping.IsEnabled,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.TenantCreated,
            TenantId = tenant.Id,
            TargetType = nameof(Tenant),
            TargetId = tenant.Id.ToString(),
            TargetName = tenant.Name,
            Metadata = new Dictionary<string, object?> { ["slug"] = tenant.Slug }
        }, ct);

        return new TenantGql
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            CreatedAt = tenant.CreatedAt
        };
    }
    
    public async Task<TenantGql> UpdateTenantAsync(UpdateTenantInput input, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, input.TenantId, TenantPermission.Admin);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(r => r.Id == input.TenantId, ct);

        if (tenant is null)
            throw new GraphQLException("Tenant not found.");

        if (input.Name.HasValue)
        {
            var newName = input.Name.Value;

            if (string.IsNullOrWhiteSpace(newName))
                throw new GraphQLException("Tenant name cannot be empty.");

            var duplicateExists = await _db.Tenants.AnyAsync(x => x.Id != input.TenantId && x.Name == newName, ct);

            if (duplicateExists)
                throw new GraphQLException($"A tenant with the name '{newName}' already exists.");

            tenant.Name = newName;
        }

        if (input.Slug.HasValue)
        {
            var newSlug = input.Slug.Value;
            
            if (string.IsNullOrWhiteSpace(newSlug))
                throw new GraphQLException("Tenant slug cannot be empty.");

            var duplicateExists = await _db.Tenants.AnyAsync(x => x.Id != input.TenantId && x.Slug == newSlug, ct);

            if (duplicateExists)
                throw new GraphQLException($"A tenant with the slug '{newSlug}' already exists.");

            tenant.Slug = newSlug;
        }
        
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.TenantUpdated,
            TenantId = tenant.Id,
            TargetType = nameof(Tenant),
            TargetId = tenant.Id.ToString(),
            TargetName = tenant.Name,
            Metadata = new Dictionary<string, object?> { ["slug"] = tenant.Slug }
        }, ct);

        return new TenantGql
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            CreatedAt = tenant.CreatedAt
        };
    }

    public async Task<bool> InviteTenantMemberAsync(InviteTenantMemberInput input, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var (user, userId) = RequireUser();
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        if (string.IsNullOrWhiteSpace(input.Email))
            throw new GraphQLException("An email address is required.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
            ?? throw new GraphQLException("Tenant not found.");

        var inviter = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new GraphQLException("Inviter user not found.");

        var existingUser = await _userManager.FindByEmailAsync(input.Email);
        if (existingUser is not null)
        {
            var alreadyMember = await _db.TenantMembers.AnyAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == existingUser.Id, ct);
            if (alreadyMember)
                throw new GraphQLException("Cannot invite member; already a member.");
        }

        var invitation = new TenantMemberInvitation
        {
            TenantId = tenantContext.TenantId,
            InviterId = userId,
            InviteeEmail = input.Email,
            Roles = input.Roles,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Code = Guid.NewGuid().ToString("N")
        };

        await _db.TenantMemberInvitations.AddAsync(invitation, ct);
        await _db.SaveChangesAsync(ct);

        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(invitation.Code));
        var acceptUrl = IdentityEndpoints.BuildFrontendUrl(_domainOptions.Value, _httpContextAccessor.HttpContext!, $"/invite/{tenantContext.TenantId:D}/{code}");
        await _emailSender.SendMemberInvitationEmailAsync(inviter, tenant, input.Email, acceptUrl);

        // The invitation code is a bearer credential and is deliberately not recorded.
        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.MemberInvited,
            TargetType = nameof(TenantMemberInvitation),
            TargetId = invitation.Id.ToString(),
            TargetName = input.Email,
            Metadata = new Dictionary<string, object?>
            {
                ["roles"] = input.Roles,
                ["expiresAt"] = invitation.ExpiresAt
            }
        }, ct);

        return true;
    }

    public async Task<TenantMemberGql> UpdateTenantMemberRolesAsync(Guid memberId, List<string> roles, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var (user, currentUserId) = RequireUser();
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        if (roles is null)
            throw new GraphQLException("Roles are required.");

        var membership = await _db.TenantMembers.FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == memberId, ct)
            ?? throw new GraphQLException("Member not found in tenant.");

        var existingRoles = await _db.TenantRoleAssignments
            .Where(r => r.TenantId == tenantContext.TenantId && r.UserId == memberId)
            .ToListAsync(ct);

        if (memberId == currentUserId)
        {
            var existingRoleNames = existingRoles.Select(r => r.RoleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requestedRoleNames = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!requestedRoleNames.IsSupersetOf(existingRoleNames))
                throw new GraphQLException("Cannot demote yourself.");
        }

        foreach (var existingRole in existingRoles)
            if (!roles.Contains(existingRole.RoleName))
                _db.TenantRoleAssignments.Remove(existingRole);

        var newRoles = roles.Where(r => existingRoles.All(x => x.RoleName != r)).Select(roleName => new TenantRoleAssignment
        {
            TenantId = tenantContext.TenantId,
            UserId = memberId,
            RoleName = roleName
        });
        await _db.TenantRoleAssignments.AddRangeAsync(newRoles, ct);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.MemberRolesUpdated,
            TargetType = nameof(TenantMember),
            TargetId = memberId.ToString(),
            Metadata = new Dictionary<string, object?> { ["roles"] = roles }
        }, ct);

        var memberUser = await _db.Users.AsNoTracking()
            .Where(u => u.Id == memberId)
            .Select(u => new { u.Email, u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct);

        return new TenantMemberGql
        {
            Id = memberId,
            Email = memberUser?.Email ?? string.Empty,
            FirstName = memberUser?.FirstName,
            LastName = memberUser?.LastName,
            JoinedAt = membership.JoinedAt,
            Roles = roles
        };
    }

    public async Task<bool> RemoveTenantMemberAsync(Guid memberId, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var (user, currentUserId) = RequireUser();
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        var membership = await _db.TenantMembers
            .FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == memberId && tm.UserId != currentUserId, ct);
        if (membership is null)
            throw new GraphQLException("Member not found in tenant.");

        var roleAssignments = await _db.TenantRoleAssignments
            .Where(r => r.TenantId == tenantContext.TenantId && r.UserId == memberId && r.UserId != currentUserId)
            .ToListAsync(ct);

        _db.TenantRoleAssignments.RemoveRange(roleAssignments);
        _db.TenantMembers.Remove(membership);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.MemberRemoved,
            TargetType = nameof(TenantMember),
            TargetId = memberId.ToString(),
            Metadata = new Dictionary<string, object?>
            {
                ["revokedRoles"] = roleAssignments.Select(r => r.RoleName).ToArray()
            }
        }, ct);

        return true;
    }

    public async Task<bool> LeaveTenantAsync(CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var (user, userId) = RequireUser();
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Member);

        var membership = await _db.TenantMembers.FirstOrDefaultAsync(tm => tm.TenantId == tenantContext.TenantId && tm.UserId == userId, ct);
        if (membership is null)
            throw new GraphQLException("You are not a member of this tenant.");

        var tenantAdmins = await _db.TenantRoleAssignments
            .Where(r => r.TenantId == tenantContext.TenantId && r.RoleName == "TenantAdmin")
            .ToListAsync(ct);
        if (tenantAdmins.Count == 1 && tenantAdmins[0].UserId == userId)
            throw new GraphQLException("Cannot leave tenant; last admin.");

        _db.TenantMembers.Remove(membership);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.MemberLeft,
            TargetType = nameof(TenantMember),
            TargetId = userId.ToString()
        }, ct);

        return true;
    }

    public async Task<bool> AcceptTenantInvitationAsync(Guid tenantId, string code, CancellationToken ct)
    {
        var (_, userId) = RequireUser();

        if (string.IsNullOrEmpty(code))
            throw new GraphQLException("Invitation code is required.");

        string decodedCode;
        try
        {
            decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch
        {
            throw new GraphQLException("Invalid invitation code.");
        }

        var invitation = await _db.TenantMemberInvitations
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Code == decodedCode && i.ExpiresAt > DateTimeOffset.UtcNow && i.AcceptedAt == null, ct);
        if (invitation is null)
            throw new GraphQLException("Invitation not found or expired.");

        var alreadyMember = await _db.TenantMembers.AnyAsync(tm => tm.TenantId == tenantId && tm.UserId == userId, ct);
        if (alreadyMember)
            throw new GraphQLException("You are already a member of this tenant.");

        await _db.TenantMembers.AddAsync(new TenantMember
        {
            TenantId = tenantId,
            UserId = userId,
            JoinedAt = DateTimeOffset.UtcNow
        }, ct);

        await _db.TenantRoleAssignments.AddRangeAsync(invitation.Roles.Select(roleName => new TenantRoleAssignment
        {
            TenantId = tenantId,
            UserId = userId,
            RoleName = roleName,
            GrantedAt = DateTimeOffset.UtcNow
        }), ct);

        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = AuditActions.InvitationAccepted,
            TenantId = tenantId,
            TargetType = nameof(TenantMember),
            TargetId = userId.ToString(),
            TargetName = invitation.InviteeEmail,
            Metadata = new Dictionary<string, object?> { ["roles"] = invitation.Roles }
        }, ct);

        return true;
    }

    public async Task<bool> DeleteTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantId, TenantPermission.Admin);

        // Parity with the previous REST endpoint, which returned 501 Not Implemented.
        throw new GraphQLException(ErrorBuilder.New()
            .SetMessage("Deleting tenants is not yet supported.")
            .SetCode("NOT_IMPLEMENTED")
            .Build());
    }
}