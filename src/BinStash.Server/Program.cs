// Copyright (C) 2025-2026  Lukas Eßmann
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
using System.Threading.Channels;
using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Repository;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Auth.Tokens;
using BinStash.Core.Entities;
using BinStash.Core.Storage;
using BinStash.Core.Storage.Gc;
using BinStash.Core.Traffic;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage;
using BinStash.Infrastructure.Templates;
using BinStash.Server.Auth.ApiKeys;
using BinStash.Server.Auth.Instance;
using BinStash.Server.Auth.Repository;
using BinStash.Server.Auth.Tenant;
using BinStash.Server.Auth.Tokens;
using BinStash.Server.Configuration;
using Microsoft.Extensions.Options;
using BinStash.Server.Context;
using BinStash.Server.Email;
using BinStash.Server.Email.Providers;
using BinStash.Server.Extensions;
using BinStash.Server.GraphQL;
using BinStash.Server.GraphQL.Features.Audit;
using BinStash.Server.GraphQL.Features.ChunkStores;
using BinStash.Server.GraphQL.Features.Instance;
using BinStash.Server.GraphQL.Features.Jobs;
using BinStash.Server.GraphQL.Features.Releases;
using BinStash.Server.GraphQL.Features.Repositories;
using BinStash.Server.GraphQL.Features.ServiceAccounts;
using BinStash.Server.GraphQL.Features.StorageClasses;
using BinStash.Server.GraphQL.Features.Tenants;
using BinStash.Server.GraphQL.Features.Traffic;
using BinStash.Server.GraphQL.Features.Usage;
using BinStash.Server.GraphQL.Features.Users;
using BinStash.Core.Auditing;
using BinStash.Server.Auditing;
using BinStash.Server.Billing;
using BinStash.Server.Grpc;
using BinStash.Server.Health;
using BinStash.Server.Helpers;
using BinStash.Server.HostedServices;
using BinStash.Server.Services.Billing;
using BinStash.Server.Middlewares;
using BinStash.Server.Services.ChunkStores;
using BinStash.Server.Services.ReleaseUpgrade;
using BinStash.Server.Services.Usage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

namespace BinStash.Server;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        builder.Services.AddHealthChecks()
            .AddNpgSql(
                connectionString: builder.Configuration.GetConnectionString("BinStashDb")!,
                name: "PostgreSQL",
                failureStatus: HealthStatus.Degraded,
                tags: ["db", "sql", "postgresql", "ready"])
            .AddCheck<ChunkStoreHealthCheck>("chunkstores_live", tags: ["live", "ready"]);

        // connection string from appsettings/env
        var connectionString = builder.Configuration.GetConnectionString("BinStashDb")
                 ?? throw new InvalidOperationException("Missing connection string");
        
        // IMPORTANT: Insert DB provider as LOWEST priority (first in list) so it can be overridden by other configuration sources
        builder.Configuration.Sources.Insert(0, new DbConfigurationSource(connectionString));
        
        builder.Services.AddSystemd();
        builder.Services.AddWindowsService();
        
        // Configuration
        builder.Services.Configure<DomainSettings>(builder.Configuration.GetSection("Domain"));
        builder.Services.Configure<TenancySettings>(builder.Configuration.GetSection("Tenancy"));
        builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
        builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));
        builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Auth:Jwt"));
        builder.Services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
        builder.Services.AddOptions<JwtSettings>().ValidateOnStart();
        builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));
        builder.Services.Configure<GarbageCollectionOptions>(builder.Configuration.GetSection(GarbageCollectionOptions.SectionName));
        builder.Services.AddSingleton<IValidateOptions<StorageSettings>, StorageSettingsValidator>();
        builder.Services.AddOptions<StorageSettings>().ValidateOnStart();
        builder.Services.Configure<VersionGateSettings>(builder.Configuration.GetSection("VersionGate"));
        builder.Services.Configure<RequestMetricsSettings>(builder.Configuration.GetSection("RequestMetrics"));
        builder.Services.Configure<SecurityHeaderSettings>(builder.Configuration.GetSection(SecurityHeaderSettings.SectionName));
        builder.Services.Configure<RequestLimitSettings>(builder.Configuration.GetSection(RequestLimitSettings.SectionName));
        builder.Services.Configure<BillingSettings>(builder.Configuration.GetSection(BillingSettings.SectionName));
        builder.Services.Configure<TrafficSettings>(builder.Configuration.GetSection(TrafficSettings.SectionName));

        var requestLimits = builder.Configuration.GetSection(RequestLimitSettings.SectionName).Get<RequestLimitSettings>() ?? new RequestLimitSettings();

        // Kestrel and gRPC both ship a default here. Setting them explicitly from configuration is
        // what makes the ingest path's appetite an operator decision rather than an inherited
        // constant nobody has looked at.
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = requestLimits.MaxRequestBodyBytes;
        });

        var securityHeaders = builder.Configuration.GetSection(SecurityHeaderSettings.SectionName).Get<SecurityHeaderSettings>() ?? new SecurityHeaderSettings();
        builder.Services.AddHsts(hsts =>
        {
            hsts.MaxAge = TimeSpan.FromDays(securityHeaders.HstsMaxAgeDays);
            hsts.IncludeSubDomains = securityHeaders.HstsIncludeSubDomains;
        });
        
        // Add services to the container.
        builder.Services.AddSingleton<IChunkStoreStorageFactory, ChunkStoreStorageFactory>();
        builder.Services.AddScoped<IChunkStoreService, ChunkStoreService>();
        builder.Services.AddScoped<ITenantFootprintCalculator, TenantFootprintCalculator>();
        builder.Services.AddScoped<ITenantFootprintPublisher, TenantFootprintPublisher>();
        builder.Services.AddScoped<ChunkStoreStatsCollector>();
        builder.Services.AddSingleton<ChunkStoreProbeCache>();
        builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>(_ => new EmailTemplateRenderer(typeof(EmailTemplateRenderer).Assembly, "BinStash.Infrastructure"));
        builder.Services.AddHttpClient<BrevoEmailProvider>();
        builder.Services.AddTransient<IEmailSender<BinStashUser>, EmailSenderImplementation>();
        builder.Services.AddTransient<ITenantEmailSender, EmailSenderImplementation>();
        builder.Services.AddTransient<IInstanceEmailTester, EmailSenderImplementation>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<TenantJoinService>();
        builder.Services.AddScoped<ChunkStoreQueryService>();
        builder.Services.AddScoped<ChunkStoreMutationService>();
        builder.Services.AddScoped<ReleaseQueryService>();
        builder.Services.AddScoped<RepositoryMutationService>();
        builder.Services.AddScoped<RepositoryQueryService>();
        builder.Services.AddScoped<ServiceAccountMutationService>();
        builder.Services.AddScoped<ServiceAccountQueryService>();
        builder.Services.AddScoped<TenantMutationService>();
        builder.Services.AddScoped<TenantQueryService>();
        builder.Services.AddScoped<UserQueryService>();
        builder.Services.AddScoped<BackgroundJobService>();
        builder.Services.AddScoped<InstanceMutationService>();
        builder.Services.AddScoped<InstanceQueryService>();
        builder.Services.AddScoped<StorageClassQueryService>();
        builder.Services.AddScoped<StorageClassMutationService>();
        builder.Services.AddScoped<AuditQueryService>();
        builder.Services.AddScoped<UsageQueryService>();
        builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        builder.Services.AddScoped<TenantUsageService>();
        builder.Services.AddScoped<TenantQuotaGuard>();

        // Traffic recording. The recorder is a singleton because it buffers across requests; the
        // store is scoped because it writes through the request-scoped DbContext.
        builder.Services.AddSingleton<BufferedTrafficRecorder>();
        builder.Services.AddSingleton<ITrafficRecorder>(sp => sp.GetRequiredService<BufferedTrafficRecorder>());
        builder.Services.AddScoped<ITrafficStore, TrafficStore>();
        builder.Services.AddScoped<TrafficQueryService>();
        builder.Services.AddNoOpBilling();
        var billingLoader = new BillingPluginLoader();
        billingLoader.LoadAndRegisterServices(builder);
        builder.Services.AddResponseCompression();
        builder.Services.AddProblemDetails();
        builder.Services.AddBinStashRateLimiting(builder.Configuration);

        builder.Services.AddCors(options =>
        {
            if (builder.Environment.IsDevelopment())
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.SetIsOriginAllowed(origin => new Uri(origin).IsLoopback)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            }
        });
        builder.Services.AddScoped<IAuthorizationHandler, InstancePermissionHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, RepositoryPermissionHandler>();
        
        builder.Services.AddScoped<IPasswordHasher<ApiKey>, PasswordHasher<ApiKey>>();
        builder.Services.AddScoped<IPasswordHasher<SetupCode>, PasswordHasher<SetupCode>>();

        builder.Services.AddDbContext<BinStashDbContext>((_, optionsBuilder) => optionsBuilder.UseNpgsql(connectionString)/*.EnableSensitiveDataLogging()*/);

        // The Data Protection key ring encrypts auth cookies and every Identity-issued token
        // (email confirmation, password reset, 2FA remember-me). Left at its default it is
        // generated per process and thrown away on exit, which signs everyone out on restart and
        // makes a token issued by one replica unverifiable on another. Persisting it to the
        // database — already a hard dependency — fixes both.
        //
        // SetApplicationName pins the isolation key. The default derives it from the content-root
        // path, so two replicas unpacked to different paths would share a table and still refuse
        // to read each other's keys. It must not change across deployments: changing it is
        // equivalent to discarding the key ring.
        builder.Services.AddDataProtection()
            .PersistKeysToDbContext<BinStashDbContext>()
            .SetApplicationName("BinStash");

        builder.Services.AddIdentityApiEndpoints<BinStashUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<BinStashDbContext>();
        
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = "Smart";
                options.DefaultChallengeScheme = "Smart";
                //options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                //options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme("Smart", "Smart Auth Scheme", options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    var auth = ctx.Request.Headers.Authorization.ToString();
                    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return JwtBearerDefaults.AuthenticationScheme;
                    if (auth.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                        return "ApiKey";
                    return IdentityConstants.ApplicationScheme;
                };
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var jwt = builder.Configuration.GetSection("Auth:Jwt").Get<JwtSettings>() ?? new JwtSettings();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("ApiKey", _ => { })
            .AddCookie("Setup", options =>
            {
                options.Cookie.Name = "binstash_setup";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                options.SlidingExpiration = true;

                options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Permission:Instance:Admin", p => p.AddRequirements(new InstancePermissionRequirement(InstancePermission.Admin)));
            
            options.AddPolicy("Permission:Tenant:Admin", p => p.AddRequirements(new TenantPermissionRequirement(TenantPermission.Admin)));
            options.AddPolicy("Permission:Tenant:BillingAdmin", p => p.AddRequirements(new TenantPermissionRequirement(TenantPermission.BillingAdmin)));
            options.AddPolicy("Permission:Tenant:Member", p => p.AddRequirements(new TenantPermissionRequirement(TenantPermission.Member)));
            
            options.AddPolicy("Permission:Repo:Admin", p => p.AddRequirements(new RepositoryPermissionRequirement(RepositoryPermission.Admin)));
            options.AddPolicy("Permission:Repo:Write", p => p.AddRequirements(new RepositoryPermissionRequirement(RepositoryPermission.Write)));
            options.AddPolicy("Permission:Repo:Read", p => p.AddRequirements(new RepositoryPermissionRequirement(RepositoryPermission.Read)));
            
            
            options.AddPolicy("Permission:Release:List", p => p.RequireClaim("Permission", "Release:List"));
            
            options.AddPolicy("SetupAuth", p => p.AddAuthenticationSchemes("Setup").RequireAuthenticatedUser().RequireClaim("setup", "true"));
        });
        
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        builder.Services.AddProblemDetails();
        
        // Hosted services
        builder.Services.AddHostedService<SetupBootstrapper>();
        builder.Services.AddHostedService<ChunkStoreProbeService>();
        builder.Services.AddHostedService<ChunkStoreStatsHostedService>();
        builder.Services.AddHostedService<TenantStorageStatsHostedService>();
        builder.Services.AddHostedService<TrafficFlushHostedService>();

        // Keeps the quota figure timely between daily sweeps without incremental accounting:
        // an ingest queues its tenant, and the walk that follows is the same full walk.
        builder.Services.AddSingleton<TenantFootprintRefreshQueue>();
        builder.Services.AddHostedService<TenantFootprintRefreshService>();
        
        // Release upgrade pipeline: Channel queue → BackgroundService → ReleaseUpgradeService
        builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        }));
        builder.Services.AddScoped<IReleaseUpgradeService, ReleaseUpgradeService>();
        builder.Services.AddHostedService<ReleaseUpgradeBackgroundService>();

        // Chunk-store rebuild pipeline: RebuildJobChannel → ChunkStoreRebuildBackgroundService → ChunkStoreRebuildService
        builder.Services.AddSingleton<RebuildJobChannel>();
        builder.Services.AddScoped<IChunkStoreRebuildService, ChunkStoreRebuildService>();
        builder.Services.AddHostedService<ChunkStoreRebuildBackgroundService>();

        // Chunk-store garbage collection: GcJobChannel → ChunkStoreGcBackgroundService → ChunkStoreGcService.
        // GcQuarantineService is on the ingest hot path too — it is what lets an ingest that was
        // told "you already have this" recover an object collection has since quarantined.
        builder.Services.AddSingleton<GcJobChannel>();
        builder.Services.AddScoped<IChunkStoreGcService, ChunkStoreGcService>();
        builder.Services.AddScoped<IGcQuarantineService, GcQuarantineService>();
        builder.Services.AddHostedService<ChunkStoreGcBackgroundService>();

        // Queues unattended runs onto that same channel. Registered unconditionally — it reads
        // its own enablement each tick, so turning the schedule on from instance settings takes
        // effect without a restart.
        builder.Services.AddHostedService<ChunkStoreGcSchedulerService>();

        builder.Services.AddGraphQLServer()
            // Cost limits are enforced: the schema exposes filtering/sorting/paging to every
            // authenticated tenant, so an unbounded query is a noisy-neighbour/DoS vector on a
            // shared (SaaS) instance. Tune the ceilings rather than turning enforcement off.
            .ModifyCostOptions(options =>
            {
                options.EnforceCostLimits = true;
                options.MaxFieldCost = 10_000;
                options.MaxTypeCost = 50_000;
            })
            .ModifyPagingOptions(options =>
            {
                options.DefaultPageSize = 25;
                options.MaxPageSize = 100;
                options.IncludeTotalCount = true;
            })
            .AddAuthorization()
            .AddQueryType<QueryType>()
            .AddMutationType<MutationType>()
            .AddSubscriptionType<SubscriptionType>()
            .AddInMemorySubscriptions()
            // Repository totals are asked for once per row on the repositories page; batching the
            // aggregate keeps that one query rather than one per repository.
            .AddDataLoader<RepositoryMetricsDataLoader>()
            .BindRuntimeType<ulong, UnsignedLongType>()
            .BindRuntimeType<ulong?, UnsignedLongType>()
            .AddFiltering()
            .AddSorting()
            .AddProjections();

        builder.Services.AddGrpc(grpc =>
        {
            grpc.MaxReceiveMessageSize = requestLimits.MaxGrpcReceiveBytes;
        });
        
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        // `--export-schema <path>` writes the GraphQL SDL and exits. The frontend's typed codegen
        // runs against a committed schema.graphql so it works offline and in CI, where no server
        // (and no database) is available to introspect. Deliberately placed before the migration
        // step below: building the schema resolves types, not data, so this must not need a DB.
        if (TryExportSchema(app, args))
            return;

        // Configure the ef core migration process
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
            if (db.Database.IsRelational())
                db.Database.Migrate(); // applies any pending migrations
        }
        
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

        // First in the pipeline so the headers reach every response, including static SPA assets
        // and anything short-circuited by the gates or the rate limiter below.
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // HSTS is deliberately not applied in Development: the dev instance and the Vite dev
        // server run on localhost, and teaching a browser to force HTTPS for localhost breaks
        // every other project on the machine that serves plain HTTP there.
        if (!app.Environment.IsDevelopment())
        {
            var headerSettings = app.Services.GetRequiredService<IOptions<SecurityHeaderSettings>>().Value;
            if (headerSettings is { Enabled: true, HstsEnabled: true })
                app.UseHsts();
        }

        app.UseCors();
        app.UseHttpsRedirection();
        app.UseResponseCompression();
        app.UseStatusCodePages();
        app.UseMiddleware<RequestMetricsMiddleware>();

        // Before the setup gate, which queries the database on every request until an instance is
        // initialized, and before authentication, so a credential-stuffing run is turned away
        // without the server hashing a password for it.
        app.UseRateLimiter();

        app.UseMiddleware<SetupGateMiddleware>();
        app.UseMiddleware<VersionGateMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // Vite fingerprints everything under /assets, so those filenames change whenever their
            // contents do and can be cached forever. index.html cannot: its name is fixed and it is
            // the file that names the current fingerprints. Served without a Cache-Control header it
            // falls to the browser's heuristic caching, which happily reuses an HTML document without
            // revalidating — so a deploy lands on the server and users keep running the previous
            // build until they force a reload. That is what happened after the 2026-09-14 deploy.
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                var path = ctx.Context.Request.Path.Value ?? string.Empty;

                if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
                    headers.CacheControl = "public, max-age=31536000, immutable";
                else
                    headers.CacheControl = "no-cache";
            }
        });
        // Three health endpoints, because they answer to three different callers.
        //
        // /health/live and /health/ready are what an orchestrator or load balancer polls, so they
        // are anonymous — a probe has no credentials to present — and they answer with a bare
        // status word. Neither reveals anything an unauthenticated caller could not already infer
        // from whether the server answers at all.
        //
        // /health keeps the detailed report (store paths, free space, exception text) and stays
        // behind instance admin, which is where that detail belongs.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            // Liveness must not depend on the database or the chunk stores: a dependency being
            // down is a reason to stop taking traffic, not a reason to have the process killed
            // and restarted into the same outage.
            Predicate = _ => false,
            ResponseWriter = WriteStatusOnlyAsync
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteStatusOnlyAsync
        }).AllowAnonymous();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";
                    var result = new
                    {
                        status = report.Status.ToString(),
                        checks = report.Entries.Select(e => new
                        {
                            name = e.Key,
                            status = e.Value.Status.ToString(),
                            description = e.Value.Description,
                            exception = e.Value.Exception?.ToString(),
                            duration = e.Value.Duration.ToString(),
                            data = e.Value.Data   // <-- add this
                        })
                    };
                    await context.Response.WriteAsJsonAsync(result);
                }
        })
        .RequireInstancePermission(InstancePermission.Admin);
        app.MapGrpcService<IngestGrpcService>();
        app.UseWebSockets();
        app.MapGraphQL();
        app.MapAllEndpoints();
        billingLoader.MapPluginEndpoints(app);
        // Every SPA route resolves here, so this is how most users actually receive index.html —
        // it needs the same no-cache treatment as the static-file path above, which it does not
        // inherit.
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
        });
        
        app.Run();
    }

    /// <summary>
    /// Writes just the aggregate status word. Used by the anonymous probe endpoints, which need
    /// the status code and nothing else — the detailed report is admin-only for a reason.
    /// </summary>
    private static Task WriteStatusOnlyAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(report.Status.ToString());
    }

    /// <summary>
    /// Handles the <c>--export-schema &lt;path&gt;</c> switch. Returns true when the schema was
    /// written and the process should exit without serving.
    /// </summary>
    private static bool TryExportSchema(WebApplication app, string[] args)
    {
        var index = Array.IndexOf(args, "--export-schema");
        if (index < 0)
            return false;

        var path = index + 1 < args.Length ? args[index + 1] : "schema.graphql";

        var executor = app.Services
            .GetRequiredService<HotChocolate.Execution.IRequestExecutorProvider>()
            .GetExecutorAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, executor.Schema.ToString());
        Console.WriteLine($"GraphQL schema written to {System.IO.Path.GetFullPath(path)}");
        return true;
    }
}
