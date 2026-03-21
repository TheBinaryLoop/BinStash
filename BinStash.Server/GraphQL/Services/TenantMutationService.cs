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
using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using BinStash.Server.GraphQL.Inputs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Services;

public sealed class TenantMutationService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public TenantMutationService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
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
        await _db.SaveChangesAsync(ct);

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

        return new TenantGql
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            CreatedAt = tenant.CreatedAt
        };
    }
}