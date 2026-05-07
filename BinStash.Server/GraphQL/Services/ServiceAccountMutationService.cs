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

using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using BinStash.Server.GraphQL.Inputs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Services;

public sealed class ServiceAccountMutationService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public ServiceAccountMutationService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }
    
    public async Task<ServiceAccountGql> CreateServiceAccountAsync(CreateServiceAccountInput input, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        if (string.IsNullOrWhiteSpace(input.Name))
            throw new GraphQLException("Service account name is required.");

        if (await _db.ServiceAccounts.AnyAsync(x => x.TenantId == tenantContext.TenantId && x.Name == input.Name, ct))
        {
            throw new GraphQLException($"A service account with the name '{input.Name}' already exists.");
        }
        
        var serviceAccount = new ServiceAccount
        {
            Name = input.Name,
            TenantId = tenantContext.TenantId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _db.ServiceAccounts.AddAsync(serviceAccount, ct);
        await _db.SaveChangesAsync(ct);

        return new ServiceAccountGql
        {
            Id = serviceAccount.Id,
            Name = serviceAccount.Name,
            CreatedAt = serviceAccount.CreatedAt
        };
    }
    
    public async Task<ServiceAccountGql> UpdateServiceAccountAsync(UpdateServiceAccountInput input, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        var serviceAccount = await _db.ServiceAccounts
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantContext.TenantId && r.Id == input.Id,
                ct);

        if (serviceAccount is null)
            throw new GraphQLException("Service account not found.");

        if (input.Name.HasValue)
        {
            var newName = input.Name.Value;

            if (string.IsNullOrWhiteSpace(newName))
                throw new GraphQLException("Service account name cannot be empty.");

            var duplicateExists = await _db.ServiceAccounts.AnyAsync(
                x => x.TenantId == tenantContext.TenantId &&
                     x.Id != input.Id &&
                     x.Name == newName,
                ct);

            if (duplicateExists)
                throw new GraphQLException($"A service account with the name '{newName}' already exists.");

            serviceAccount.Name = newName;
        }

        /*if (input.Description.HasValue)
        {
            // null means: clear description
            serviceAccount.Description = input.Description.Value;
        }*/

        await _db.SaveChangesAsync(ct);

        return new ServiceAccountGql
        {
            Id = serviceAccount.Id,
            Name = serviceAccount.Name,
            CreatedAt = serviceAccount.CreatedAt
        };
    }
    
    public async Task<bool> DeleteServiceAccountAsync(Guid serviceAccountId, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Admin);

        var serviceAccount = await _db.ServiceAccounts
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantContext.TenantId && r.Id == serviceAccountId,
                ct);

        if (serviceAccount is null)
            throw new GraphQLException("Service account not found.");

        _db.ServiceAccounts.Remove(serviceAccount);
        await _db.SaveChangesAsync(ct);

        return true;
    }
}