// Copyright (C) 2025  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Auth.Tokens;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Email.Brevo;
using BinStash.Infrastructure.Templates;
using BinStash.Server.Auth.ApiKeys;
using BinStash.Server.Auth.Repository;
using BinStash.Server.Auth.Tenant;
using BinStash.Server.Auth.Tokens;
using BinStash.Server.Configuration.Tenancy;
using BinStash.Server.Context;
using BinStash.Server.Email;
using BinStash.Server.Extensions;
using BinStash.Server.HostedServices;
using BinStash.Server.Middlewares;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

namespace BinStash.Server;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSystemd();
        builder.Services.AddWindowsService();
        
        // Configuration
        builder.Services.Configure<TenancyOptions>(builder.Configuration.GetSection("Tenancy"));
        
        // Add services to the container.
        builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>(_ => new EmailTemplateRenderer(typeof(EmailTemplateRenderer).Assembly, "BinStash.Infrastructure"));
        builder.Services.AddHttpClient<BrevoApiClient>();
        builder.Services.AddTransient<BrevoApiClient>();
        builder.Services.AddTransient<IEmailSender<BinStashUser>, BrevoEmailSender>();
        builder.Services.AddTransient<ITenantEmailSender, BrevoEmailSender>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<TenantJoinService>();
        builder.Services.AddResponseCompression();
        builder.Services.AddProblemDetails();
        
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Auth:Jwt:Issuer"],
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Auth:Jwt:Key"] ?? "dev-only-change-me")),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("ApiKey", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Permission:Tenant:Admin", p => p.AddRequirements(new TenantPermissionRequirement(TenantPermission.Admin)));
            options.AddPolicy("Permission:Tenant:Member", p => p.AddRequirements(new TenantPermissionRequirement(TenantPermission.Member)));
            
            options.AddPolicy("Permission:Repo:Admin", p => p.AddRequirements(new RepositoryPermissionRequirement(RepositoryPermission.Admin)));
            options.AddPolicy("Permission:Repo:Write", p => p.AddRequirements(new RepositoryPermissionRequirement(RepositoryPermission.Write)));
            options.AddPolicy("Permission:Repo:Read", p => p.AddRequirements(new RepositoryPermissionRequirement(RepositoryPermission.Read)));
            
            
            options.AddPolicy("Permission:Release:List", p => p.RequireClaim("Permission", "Release:List"));
        });
        builder.Services.AddScoped<IAuthorizationHandler, RepositoryPermissionHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
        
        builder.Services.AddScoped<IPasswordHasher<ApiKey>, PasswordHasher<ApiKey>>();

        builder.Services.AddDbContext<BinStashDbContext>((_, optionsBuilder) => optionsBuilder.UseNpgsql(builder.Configuration.GetConnectionString("BinStashDb"))/*.EnableSensitiveDataLogging()*/);

        builder.Services.AddIdentityApiEndpoints<BinStashUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<BinStashDbContext>();
        
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        builder.Services.AddProblemDetails();
        
        // Hosted services
        builder.Services.AddHostedService<SingleTenantBootstrapper>();

        
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();
        
        // Configure the ef core migration process
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
            db.Database.Migrate(); // applies any pending migrations
        });
        
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options => options
                .AddPreferredSecuritySchemes("BearerAuth")
                .AddHttpAuthentication("BearerAuth", scheme =>
                {
                    scheme.Description = "Standard Bearer authentication";
                    scheme.Token = "exampletoken12345";
                }));
        }

        app.UseHttpsRedirection();
        app.UseResponseCompression();
        app.UseStatusCodePages();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAllEndpoints();
        
        app.Run();
    }
}