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
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using BinStash.Server.Configuration.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Endpoints;

public static class SetupEndpoints
{
    public static RouteGroupBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        /* Setup API and workflow:
         * 1. Claim setup code (anonymous) - issues setup cookie
         * 2. Authenticated setup steps:
         * 3. Set tenancy mode (Single/Multi)
         * 4. If Single: Configure single tenant
         * 5. If Multi: (skip)
         * 6. Ensure local chunk-store
         * 7. Create storage class(es)
         * 8. If Multi: Configure default storage class mappings
         * 9. If Single: Configure storage mappings for single tenant
         * 10. If Multi: Create instance admin user
         * 11. If Single: Create tenant admin user
         * 12. Review details (continue or start over)
         * 13. Finish setup
         */
        
        // TODO: If instance admin user: Show instance admin dashboard and stuff
        
        var group = app.MapGroup("/api/setup")
            .WithTags("Setup");

        group.MapPost("/claim", ClaimAsync)
            .AllowAnonymous();
        group.MapGet("/status", StatusAsync)
            .WithDescription("Get current setup status.")
            .AllowAnonymous();
        
        group.MapPost("/tenancy", SetTenancyAsync)
            .WithDescription("Set tenancy mode for this instance.")
            .RequireAuthorization("SetupAuth");
        group.MapGet("/chunk-store/enabled-types", GetChunkStoreTypes)
            .WithDescription("Get available chunk store types.")
            .RequireAuthorization("SetupAuth");
        group.MapPost("/chunk-store", EnsureChunkStoreAsync).RequireAuthorization("SetupAuth");
        group.MapPost("/storage-class", EnsureStorageClassesAsync).RequireAuthorization("SetupAuth");
        group.MapPost("/storage/defaults", EnsureStorageDefaultsAsync).RequireAuthorization("SetupAuth");
        group.MapPost("/admin", CreateAdminAsync).RequireAuthorization("SetupAuth");
        group.MapPost("/finish", FinishAsync).RequireAuthorization("SetupAuth");
        group.MapPost("/logout", LogoutSetupAsync)
            .WithDescription("Logout from setup session.")
            .RequireAuthorization("SetupAuth");

        return group;
    }

    private static async Task<IResult> ClaimAsync(ClaimRequest req, HttpContext http, BinStashDbContext db, IPasswordHasher<SetupCode> hasher)
    {
        var state = await db.SetupStates.SingleOrDefaultAsync(x => x.Id == 1);
        if (state is null || state.IsInitialized)
            return Results.Conflict("already_initialized");

        var code = await db.SetupCodes.SingleOrDefaultAsync(x => x.Id == 1);
        if (code is null)
            return Results.Problem("Setup code not available. Check server logs.", statusCode: 500);

        var now = DateTimeOffset.UtcNow;

        if (code.LockedUntil is not null && code.LockedUntil > now)
            return Results.Problem("Too many attempts. Try again later.", statusCode: 429);

        if (code.ExpiresAt <= now)
            return Results.Problem("Setup code expired. Restart service to generate a new one.", statusCode: 400);

        var vr = hasher.VerifyHashedPassword(code, code.CodeHash, req.Code);
        if (vr == PasswordVerificationResult.Failed)
        {
            code.AttemptCount++;
            if (code.AttemptCount >= 10)
                code.LockedUntil = now.AddMinutes(10);

            await db.SaveChangesAsync();
            return Results.Unauthorized();
        }

        // consume code
        code.ConsumedAt = now;
        state.ClaimedAt = now;
        await db.SaveChangesAsync();

        // sign in setup cookie
        var identity = new ClaimsIdentity("Setup");
        identity.AddClaim(new Claim("setup", "true"));
        identity.AddClaim(new Claim("setupVersion", state.SetupVersion.ToString()));

        await http.SignInAsync("Setup", new ClaimsPrincipal(identity));

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> StatusAsync(HttpContext http, BinStashDbContext db)
    {
        var state = await db.SetupStates.AsNoTracking().SingleAsync(x => x.Id == 1);
        
        // Check if signed in and the setup claim is present and true
        var authResult = await http.AuthenticateAsync("Setup");
        var isSetupAuthenticated = authResult.Succeeded
                        && authResult.Principal?.FindFirstValue("setup") == "true";
        
        if (!isSetupAuthenticated)
        {
            // Not authenticated for setup, only return limited status
            if (state.IsInitialized)
                return Results.Ok(new
                {
                    state.IsInitialized
                });
            return Results.Ok(new
            {
                state.IsInitialized,
                CurrentStep = "Claim"
            });
        }
        
        return Results.Ok(new
        {
            state.IsInitialized,
            state.CurrentStep,
            state.SetupVersion,
            Data = new
            {
                state.TenancyMode,
                ChunkStores = db.ChunkStores.Select(x => new { x.Id, x.Name, Type = x.Type, x.LocalPath }).ToList(),
                StorageClasses = db.StorageClasses.Select(x => new { x.Name, x.DisplayName, x.Description }).ToList(),
                StorageClassDefaultMappings = db.StorageClassDefaultMappings.Select(x => new { x.StorageClassName, x.ChunkStoreId, x.IsDefault, x.IsEnabled }).ToList(),
                Tenants = db.Tenants.Select(x => new { x.Id, x.Name, x.Slug }).ToList(),
                InstanceAdmins = db.Users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == db.Roles.Where(r => r.Name == "InstanceAdmin").Select(r => r.Id).FirstOrDefault()))
                    .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }).ToList(),
                TenantAdmins = db.Users.Where(u => db.TenantRoleAssignments.Any(tr => tr.UserId == u.Id && tr.RoleName == "TenantAdmin"))
                    .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }).ToList()
            }
        });
    }

    private static async Task<IResult> SetTenancyAsync(SetTenancyRequest req, BinStashDbContext db, IConfiguration config, IOptions<TenancyOptions> tenancyOpts)
    {
        var state = await db.SetupStates.SingleAsync(x => x.Id == 1);
        if (state.IsInitialized) return Results.Conflict("already_initialized");

        // effective mode (appsettings/env overrides DB due to provider ordering)
        // but during setup we write to DB when not set in appsettings.
        var locked = !string.IsNullOrWhiteSpace(config["Tenancy:Mode"]);

        var mode = locked
            ? tenancyOpts.Value.Mode.ToString()
            : req.Mode;

        if (mode is not ("Single" or "Multi"))
            return Results.BadRequest("Mode must be 'Single' or 'Multi'.");

        if (!locked)
            await UpsertSetting(db, "Tenancy:Mode", mode);

        state.TenancyMode = mode;
        state.CurrentStep = mode switch
        {
            "Single" => "DefaultTenant",
            "Multi" => "ChunkStore",
            _ => state.CurrentStep
        };
        await db.SaveChangesAsync();

        ReloadConfig(config);

        return Results.Ok(new { mode, locked });
    }

    private static IResult GetChunkStoreTypes()
    {
        var types = Enum.GetValues<ChunkStoreType>()
            .Select(x => new { name = x.ToString(), value = (int)x })
            .ToList();
        return Results.Ok(types);
    }
    
    private static async Task<IResult> EnsureChunkStoreAsync(EnsureChunkStoreRequest req, BinStashDbContext db)
    {
        var state = await db.SetupStates.SingleAsync(x => x.Id == 1);
        if (state.IsInitialized) 
            return Results.Conflict("already_initialized");
        
        if (req.Skip)
        {
            if (!await db.ChunkStores.AnyAsync())
                return Results.BadRequest("No chunk store exists to skip to next step.");
            state.CurrentStep = "StorageClass";
            await db.SaveChangesAsync();
            return Results.Ok(new { });
        }

        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name required.");
        if (string.IsNullOrWhiteSpace(req.LocalPath))
            return Results.BadRequest("LocalPath required.");

        if (!Directory.Exists(req.LocalPath))
            Directory.CreateDirectory(req.LocalPath);

        // idempotent by name
        var existing = await db.ChunkStores.SingleOrDefaultAsync(x => x.Name == req.Name);
        if (existing is null)
        {
            var store = new ChunkStore(req.Name, req.Type, req.LocalPath, new LocalFolderObjectStorage(req.LocalPath)); // TODO: Support other types
            db.ChunkStores.Add(store);
            await db.SaveChangesAsync();
            existing = store;
        }

        state.CurrentStep = "StorageClass";
        await db.SaveChangesAsync();

        return Results.Ok(new { chunkStoreId = existing.Id });
    }
    
    private static async Task<IResult> EnsureStorageClassesAsync(EnsureStorageClassesRequest req, BinStashDbContext db)
    {
        var state = await db.SetupStates.SingleAsync(x => x.Id == 1);
        if (state.IsInitialized)
            return Results.Conflict("already_initialized");

        if (req.StorageClasses.Count == 0)
            return Results.BadRequest("At least one storage class is required.");

        foreach (var scReq in req.StorageClasses)
        {
            if (string.IsNullOrWhiteSpace(scReq.Name))
                return Results.BadRequest("Name required for each storage class.");

            // idempotent by name
            var existing = await db.StorageClasses.SingleOrDefaultAsync(x => x.Name == scReq.Name);
            if (existing is null)
            {
                var sc = new StorageClass
                {
                    Name = scReq.Name.ToLowerInvariant(),
                    DisplayName = scReq.DisplayName,
                    Description = string.IsNullOrEmpty(scReq.Description) ? "Created during setup" : scReq.Description
                };
                db.StorageClasses.Add(sc);
                await db.SaveChangesAsync();
            }
        }
        
        state.CurrentStep = "StorageClassDefaultMappings";
        await db.SaveChangesAsync();
        return Results.Ok(new { });
    }
    
    private static async Task<IResult> EnsureStorageDefaultsAsync(EnsureStorageDefaultsRequest req, BinStashDbContext db, IOptions<TenancyOptions> tenancyOpts)
    {
        if (req.StorageClassDefaultMappings.Count == 0) 
            return Results.BadRequest("At least one storage class default mapping is required.");
        var state = await db.SetupStates.SingleAsync(x => x.Id == 1);
        if (state.IsInitialized) return Results.Conflict("already_initialized");

        var mode = state.TenancyMode ?? tenancyOpts.Value.Mode.ToString();
        //if (store is null) return Results.BadRequest("No ChunkStore exists.");
        //if ()

        foreach (var mapping in req.StorageClassDefaultMappings)
        {
            db.StorageClassDefaultMappings.Add(new StorageClassDefaultMapping
            {
                StorageClassName = mapping.StorageClassName,
                ChunkStoreId = mapping.ChunkStoreId,
                IsDefault = mapping.IsDefault,
                IsEnabled = mapping.IsEnabled,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();

        if (mode == "Single")
        {
            var tenantId = tenancyOpts.Value.SingleTenant.TenantId;
            if (tenantId == Guid.Empty)
                return Results.BadRequest("Single tenant not configured yet (TenantId missing).");

            var anyMapping = await db.StorageClassMappings.AnyAsync(x => x.TenantId == tenantId);
            if (!anyMapping)
            {
                foreach (var storageClassDefaultMapping in db.StorageClassDefaultMappings)
                {
                    db.StorageClassMappings.Add(new StorageClassMapping
                    {
                        TenantId = tenantId,
                        StorageClassName = storageClassDefaultMapping.StorageClassName,
                        ChunkStoreId = storageClassDefaultMapping.ChunkStoreId,
                        IsDefault = storageClassDefaultMapping.IsDefault,
                        IsEnabled = storageClassDefaultMapping.IsEnabled,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
                await db.SaveChangesAsync();
            }
        }

        state.CurrentStep = "InstanceAdmin";
        await db.SaveChangesAsync();
        return Results.Ok(new {});
    }

    private static async Task<IResult> CreateAdminAsync(
        CreateAdminRequest req,
        BinStashDbContext db,
        UserManager<BinStashUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IOptions<TenancyOptions> tenancyOpts
    )
    {
        var state = await db.SetupStates.SingleAsync(x => x.Id == 1);
        if (state.IsInitialized) return Results.Conflict("already_initialized");
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest("Email and Password required.");

        var existing = await users.FindByEmailAsync(req.Email);
        if (existing is not null) return Results.Conflict("User already exists.");

        var user = new BinStashUser
        {
            Id = Guid.CreateVersion7(),
            Email = req.Email,
            UserName = req.Email,
            EmailConfirmed = true,
            FirstName = req.FirstName ?? "Admin",
            LastName = req.LastName ?? "User",
            OnboardingCompleted = true
        };

        var cr = await users.CreateAsync(user, req.Password);
        if (!cr.Succeeded)
            return Results.BadRequest(cr.Errors);

        var mode = state.TenancyMode ?? tenancyOpts.Value.Mode.ToString();

        if (req.IsTenantAdmin)
        {
            var tenantId = tenancyOpts.Value.SingleTenant.TenantId;
            if (tenantId == Guid.Empty)
                return Results.BadRequest("Single tenant not configured yet (TenantId missing).");

            // membership
            if (!await db.TenantMembers.AnyAsync(x => x.TenantId == tenantId && x.UserId == user.Id))
            {
                db.TenantMembers.Add(new TenantMember
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    JoinedAt = DateTimeOffset.UtcNow
                });
            }

            // role assignment
            if (!await db.TenantRoleAssignments.AnyAsync(x => x.TenantId == tenantId && x.UserId == user.Id && x.RoleName == "TenantAdmin"))
            {
                db.TenantRoleAssignments.Add(new TenantRoleAssignment
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    RoleName = "TenantAdmin",
                    GrantedAt = DateTimeOffset.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }
        
        if (req.IsInstanceAdmin)
        {
            const string roleName = "InstanceAdmin";
            if (!await roles.RoleExistsAsync(roleName))
                await roles.CreateAsync(new IdentityRole<Guid>(roleName));
            
            await users.AddToRoleAsync(user, roleName);
        }

        state.CurrentStep = mode == "Single" && req.IsInstanceAdmin ? "TenantAdmin" : "Review";
        await db.SaveChangesAsync();
        return Results.Ok(new {});
    }

    private static async Task<IResult> FinishAsync(
        BinStashDbContext db,
        IConfiguration config,
        IOptions<TenancyOptions> tenancyOpts
    )
    {
        var state = await db.SetupStates.SingleAsync(x => x.Id == 1);
        if (state.IsInitialized) return Results.Conflict("already_initialized");

        var mode = state.TenancyMode ?? tenancyOpts.Value.Mode.ToString();
        if (mode is not ("Single" or "Multi"))
            return Results.BadRequest("Tenancy mode not set.");

        // invariants
        if (!await db.ChunkStores.AnyAsync())
            return Results.BadRequest("At least one chunk-store required.");

        if (!await db.StorageClasses.AnyAsync())
            return Results.BadRequest("At least one storage class required.");

        if (mode == "Single")
        {
            var tenantId = tenancyOpts.Value.SingleTenant.TenantId;
            if (tenantId == Guid.Empty) return Results.BadRequest("Single tenant TenantId missing.");
            if (!await db.Tenants.AnyAsync(x => x.Id == tenantId)) return Results.BadRequest("Tenant missing.");
            if (!await db.StorageClassMappings.AnyAsync(x => x.TenantId == tenantId && x.IsDefault))
                return Results.BadRequest("Default storage mapping missing for tenant.");
            if (!await db.TenantRoleAssignments.AnyAsync(x => x.TenantId == tenantId && x.RoleName == "TenantAdmin"))
                return Results.BadRequest("No tenant admin exists.");
        }
        else
        {
            if (!await db.StorageClassDefaultMappings.AnyAsync(x => x.IsDefault))
                return Results.BadRequest("Default storage mapping template missing.");
            // Instance admin existence check is role-based; optional here
        }

        state.IsInitialized = true;
        state.CompletedAt = DateTimeOffset.UtcNow;
        state.SetupVersion += 1;
        state.CurrentStep = "Done";
        await db.SaveChangesAsync();

        ReloadConfig(config);

        return Results.Ok(new { ok = true });
    }
    
    private static async Task<IResult> LogoutSetupAsync(HttpContext http, IConfiguration _)
    {
        await http.SignOutAsync("Setup");
        return Results.Ok(new { ok = true });
    }

    private static async Task UpsertSetting(BinStashDbContext db, string key, string value)
    {
        var existing = await db.InstanceSettings.SingleOrDefaultAsync(x => x.Key == key);
        if (existing is null)
        {
            db.InstanceSettings.Add(new InstanceSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static void ReloadConfig(IConfiguration cfg)
    {
        if (cfg is IConfigurationRoot root)
            root.Reload();
    }

    // DTOs
    public sealed record ClaimRequest(string Code);
    public sealed record SetTenancyRequest(string Mode);
    public sealed record EnsureChunkStoreRequest(ChunkStoreType Type, string Name, string LocalPath, bool Skip = false);
    public sealed record EnsureStorageClassRequest(string Name, string DisplayName, string? Description);
    public sealed record EnsureStorageClassesRequest(List<EnsureStorageClassRequest> StorageClasses);
    public sealed record EnsureStorageDefaultsRequest(List<StorageClassDefaultMappingReq> StorageClassDefaultMappings);
    public sealed record StorageClassDefaultMappingReq(Guid ChunkStoreId, string StorageClassName, bool IsDefault, bool IsEnabled);
    public sealed record CreateAdminRequest(bool IsTenantAdmin, bool IsInstanceAdmin, string Email, string Password, string? FirstName, string? LastName);
}