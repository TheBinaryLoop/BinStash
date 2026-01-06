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

using BinStash.Contracts.Auth;
using BinStash.Contracts.ServiceAccount;
using BinStash.Core.Auth;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Auth.Tokens;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Context;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class ServiceAccountEndpoints
{
    public static RouteGroupBuilder MapServiceAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var tenantGroup = app.MapGroup("/api/tenants/{tenantId:guid}/service-accounts")
            .WithTags("Service Accounts")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization()
            .RequireTenantPermission(TenantPermission.Admin);
        
        var group = app.MapGroup("/api/service-accounts")
            .WithTags("Service Accounts")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization()
            .RequireTenantPermission(TenantPermission.Admin);
        
        MapGroup(tenantGroup);
        MapGroup(group);
        
        return group;
    }

    private static void MapGroup(RouteGroupBuilder group)
    {
        group.MapGet("/", GetServiceAccountsAsync)
            .WithDescription("Get all service accounts for the tenant.")
            .WithSummary("Get Service Accounts")
            .Produces<List<ServiceAccountInfoDto>>();

        group.MapPost("/", CreateServiceAccountAsync)
            .WithDescription("Create a new service account.")
            .WithSummary("Create Service Account")
            .Produces<ServiceAccountInfoDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);
        
        group.MapDelete("/{serviceAccountId:guid}", DeleteServiceAccountAsync)
            .WithDescription("Delete a service account.")
            .WithSummary("Delete Service Account")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{serviceAccountId:guid}/api-keys", CreateApiKeyForServiceAccountAsync)
            .WithDescription("Create a new API key for the service account.")
            .WithSummary("Create API Key for Service Account")
            .Produces<CreateApiKeyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{serviceAccountId:guid}/api-keys", GetApiKeyForServiceAccountAsync)
            .WithDescription("Retrieve all API keys for the service account.")
            .WithSummary("Retrieve API Keys for Service Account")
            .Produces<List<ApiKeyInfoDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        
        group.MapDelete("/{serviceAccountId:guid}/api-keys/{apiKeyId:guid}", DeleteApiKeyForServiceAccountAsync)
            .WithDescription("Delete an API key for the service account.")
            .WithSummary("Delete API Key for Service Account")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
    
    private static async Task<IResult> GetServiceAccountsAsync(BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        var tenantId = tenantContext.TenantId;
        var serviceAccounts = await db.ServiceAccounts
            .Where(sa => sa.TenantId == tenantId)
            .Select(sa => new ServiceAccountInfoDto(sa.Id, sa.Name, sa.CreatedAt))
            .ToListAsync(context.RequestAborted);
        return Results.Ok(serviceAccounts);
    }
    
    private static async Task<IResult> CreateServiceAccountAsync(CreateServiceAccountDto request, BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("Service account name is required.");
        
        var tenantId = tenantContext.TenantId;
        
        if (await db.ServiceAccounts.AnyAsync(sa => sa.TenantId == tenantId && sa.Name == request.Name, context.RequestAborted))
            return Results.Conflict($"A service account with the name '{request.Name}' already exists.");
        
        var serviceAccount = new ServiceAccount
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ServiceAccounts.Add(serviceAccount);
        await db.SaveChangesAsync(context.RequestAborted);
        var dto = new ServiceAccountInfoDto(serviceAccount.Id, serviceAccount.Name, serviceAccount.CreatedAt);
        return Results.Created($"/api/service-accounts/{serviceAccount.Id}", dto);
    }
    
    private static async Task<IResult> DeleteServiceAccountAsync(Guid serviceAccountId, BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        var tenantId = tenantContext.TenantId;
        var serviceAccount = await db.ServiceAccounts.FindAsync(serviceAccountId);
        if (serviceAccount == null || serviceAccount.TenantId != tenantId)
            return Results.NotFound();
        
        var apiKeys = await db.ApiKeys.Where(x => x.SubjectType == SubjectType.ServiceAccount && x.SubjectId == serviceAccountId).ToListAsync(context.RequestAborted);
        db.ApiKeys.RemoveRange(apiKeys);
        
        var roleAssignments = await db.RepositoryRoleAssignments
            .Where(x => x.SubjectType == SubjectType.ServiceAccount && x.SubjectId == serviceAccountId)
            .ToListAsync(context.RequestAborted);
        db.RepositoryRoleAssignments.RemoveRange(roleAssignments);
        
        db.ServiceAccounts.Remove(serviceAccount);
        
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.NoContent();
    }
    
    private static async Task<IResult> CreateApiKeyForServiceAccountAsync(Guid serviceAccountId, CreateApiKeyRequest request, BinStashDbContext db, HttpContext context, TenantContext tenantContext, ITokenService tokenService)
    {
        var tenantId = tenantContext.TenantId;
        var serviceAccount = await db.ServiceAccounts.FindAsync(serviceAccountId);
        if (serviceAccount == null || serviceAccount.TenantId != tenantId)
            return Results.NotFound();
        
        var (apiKey, rawApiKey) = await tokenService.CreateApiKeyAsync(SubjectType.ServiceAccount, serviceAccount.Id, request.DisplayName, request.ExpiresAt);
        
        return Results.Created($"/api/service-accounts/{serviceAccountId}/api-keys/{apiKey.Id}", new CreateApiKeyResponse(apiKey.DisplayName, $"{Convert.ToHexStringLower(apiKey.Id.ToByteArray())}.{rawApiKey}", apiKey.ExpiresAt));
    }
    
    private static async Task<IResult> GetApiKeyForServiceAccountAsync(Guid serviceAccountId, BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        var tenantId = tenantContext.TenantId;
        var serviceAccount = await db.ServiceAccounts.FindAsync(serviceAccountId);
        if (serviceAccount == null || serviceAccount.TenantId != tenantId)
            return Results.NotFound();
        
        var apiKeys = await db.ApiKeys
            .Where(x => x.SubjectType == SubjectType.ServiceAccount && x.SubjectId == serviceAccountId)
            .Select(x => new ApiKeyInfoDto(x.Id, x.DisplayName, x.CreatedAt, x.ExpiresAt, x.LastUsedAt, x.IsActive))
            .ToListAsync(context.RequestAborted);
        
        return Results.Ok(apiKeys);
    }

    private static async Task<IResult> DeleteApiKeyForServiceAccountAsync(Guid serviceAccountId, Guid apiKeyId, BinStashDbContext db, HttpContext context, TenantContext tenantContext)
    {
        var tenantId = tenantContext.TenantId;
        var serviceAccount = await db.ServiceAccounts.FindAsync(serviceAccountId);
        if (serviceAccount == null || serviceAccount.TenantId != tenantId)
            return Results.NotFound();
        
        var apiKey = await db.ApiKeys
            .Where(x => x.SubjectType == SubjectType.ServiceAccount && x.SubjectId == serviceAccountId && x.Id == apiKeyId)
            .SingleOrDefaultAsync(context.RequestAborted);
        
        if (apiKey == null)
            return Results.NotFound();
        
        db.ApiKeys.Remove(apiKey);
        await db.SaveChangesAsync(context.RequestAborted);
        
        return Results.NoContent();
    }
}