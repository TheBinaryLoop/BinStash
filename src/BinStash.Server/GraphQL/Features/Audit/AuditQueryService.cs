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

using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Tenant;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.Audit;

public sealed class AuditQueryService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public AuditQueryService(
        BinStashDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    /// <summary>Audit trail for the resolved tenant. Tenant admins only.</summary>
    public async Task<IQueryable<AuditLogEntryGql>> GetTenantAuditLogAsync()
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = RequireUser();

        await GraphQlAuth.EnsureTenantPermissionAsync(
            user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        return Project(_db.AuditLogEntries.Where(e => e.TenantId == tenantContext.TenantId));
    }

    /// <summary>
    /// Instance-wide audit trail, including entries that belong to no tenant (SMTP changes,
    /// tenancy mode, storage defaults). Instance admins only.
    /// </summary>
    public async Task<IQueryable<AuditLogEntryGql>> GetInstanceAuditLogAsync()
    {
        await GraphQlAuth.EnsureInstancePermissionAsync(
            RequireUser(), _authorizationService, InstancePermission.Admin);

        return Project(_db.AuditLogEntries);
    }

    private static IQueryable<AuditLogEntryGql> Project(IQueryable<Core.Entities.AuditLogEntry> source)
        => source
            .AsNoTracking()
            .Select(e => new AuditLogEntryGql
            {
                Id = e.Id,
                TenantId = e.TenantId,
                OccurredAt = e.OccurredAt,
                Action = e.Action,
                ActorType = e.ActorType,
                ActorId = e.ActorId,
                ActorDisplay = e.ActorDisplay,
                TargetType = e.TargetType,
                TargetId = e.TargetId,
                TargetName = e.TargetName,
                Outcome = e.Outcome,
                IpAddress = e.IpAddress,
                Metadata = e.Metadata
            });

    private System.Security.Claims.ClaimsPrincipal RequireUser()
        => _httpContextAccessor.HttpContext?.User
           ?? throw new GraphQLException("No user context.");
}
