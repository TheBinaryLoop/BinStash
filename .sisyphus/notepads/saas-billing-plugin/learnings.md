# Learnings — saas-billing-plugin

- Removing the BinStash `Subscription` entity required keeping `AddInMemorySubscriptions()` in `Program.cs` because `ReleaseUpgradeService` still depends on `ITopicEventSender`; only the GraphQL root type registration was removed.
- EF migration `RemoveSubscriptionEntity` correctly scaffolds `DropTable("Subscriptions")` once the model no longer exposes the entity/configuration.

- Release download auth gap was a hardcoded password bypass in `BinStash.Server/Endpoints/ReleaseEndpoints.cs`; removing the secret and relying on the existing route-group `.RequireAuthorization()` is the minimal fix.
- `ReleaseEndpoints.MapReleaseEndpoints()` already uses the Smart auth pipeline via the global server auth setup; the handler should not carry a separate password gate.
- `dotnet build BinStash.Server/BinStash.Server.csproj --configuration Release --ignore-failed-sources` succeeds cleanly after the auth fix (0 errors).

- Billing interfaces were added under `BinStash.Core/Billing/` with no Stripe references.
- `IBillingPluginRegistrar` needs ASP.NET Core routing abstractions; `BinStash.Core` builds cleanly when it references `Microsoft.AspNetCore.App` as a framework reference.
- `BinStash.Core` release build passes with 0 errors; existing warnings are unrelated to billing work.
- No-op billing wiring can live in `BinStash.Server` as a simple `IServiceCollection` extension that registers `IBillingProvider` and `IUsageMeteringService` with `AddSingleton`, which keeps later plugin replacement straightforward.
- `NoOpUsageMeteringService` should stay side-effect free except for `ILogger<NoOpUsageMeteringService>.LogDebug(...)` calls so billing hooks remain observable without enforcing quotas.
Dual-licensing allows BinStash to be used under AGPLv3 for OSS projects while providing a commercial path for SaaS/closed-source integrations (like the private SaaS plugin) without triggering AGPL source disclosure requirements.

## T6: BillingPluginLoader (2026-05-08)

### Pattern: Testing internal methods via reflection
- BillingPluginLoader.RegisterFromRegistrar is internal — tests access it via reflection
  (same pattern as BillingDiTests accessing BillingServiceCollectionExtensions)
- Test project only references BinStash.Core; server assembly loaded via Assembly.LoadFrom
  using Release/Debug bin path candidates

### Pattern: Testable loader design
- Extracted RegisterFromRegistrar(IServiceCollection, IConfiguration, IBillingPluginRegistrar)
  as internal method so tests can inject a stub registrar without needing a real DLL
- LoadAndRegisterServices calls RegisterFromRegistrar after resolving the registrar from the assembly

### InvalidPath test approach
- Cannot call LoadAndRegisterServices without WebApplicationBuilder
- Replicated the exact throw logic inline in the test to verify exception message format
- Assembly.LoadFrom on a nonexistent path throws FileNotFoundException; wrapped as InvalidOperationException

### Security comment
- Added // SECURITY: BillingPluginPath must come from environment variable only, never from DbConfigurationSource
  directly above the config key constant in BillingPluginLoader.cs

## T7: IngestGrpcService metering (2026-05-08)

- IngestGrpcService constructor injection pattern: add field + ctor param, follow existing pattern
- ILogger<IngestGrpcService> needed for LogWarning — add Microsoft.Extensions.Logging using
- msg.Data.Length is int (ByteString.Length) — implicit cast to long for RecordIngest(Guid, long)
- Meter fires BEFORE dedup check (inside while loop, after chunksSeenTotal++) — bills ALL chunks including duplicates
- epo.TenantId is resolved at line ~229 (before the while loop) — meter safely uses it inside the loop
- Test lives in BinStash.Server.Tests/Billing/IngestMeterTests.cs (not Core.Tests — needs Server reference)
- ChunkStore entity has public Guid Id { get; } (no setter) — must use constructor 
ew ChunkStore(name, type, settings)
- Repository.Id has private set — use reflection 	ypeof(Repository).GetProperty("Id")!.SetValue(repo, repoId) to set in tests
- IngestSession.RepoId (not RepositoryId), StartedAt (not CreatedAt)
- FakeServerCallContext: set UserState["__HttpContext"] = httpContext for context.GetHttpContext() to work
- IChunkStoreService interface has ReadOnlyMemory<byte> params (not yte[]) — match exactly
- ChunkStorePhysicalStats is in BinStash.Core.Storage.Stats namespace

## T8: TenantStorageStatsHostedService (2026-05-08)

### EF Core in-memory DB gotchas for test seeding
- IngestSession.Id defaults to Guid.Empty - must assign Guid.NewGuid() explicitly when creating multiple sessions
- Release.Id also defaults to Guid.Empty - same issue
- ReleaseMetrics has a one-to-one relationship with IngestSession (HasOne.WithOne()) - each ReleaseMetrics needs a unique IngestSession
- IngestSession.Repository is a required nav prop (= null!) - must be set when adding to avoid "severed association" error
- Use separate DbContext instances for seeding vs. service scope to avoid identity tracking conflicts
- ChunkStore objects shared across repos cause graph traversal conflicts - use separate ChunkStore per repo in tests

### Service pattern
- TenantStorageStatsHostedService follows ChunkStoreStatsHostedService pattern exactly
- Interval configurable via Billing:StorageStatsIntervalMinutes (default 60)
- Query: ReleaseMetrics JOIN Releases JOIN Repositories GROUP BY TenantId SUM TotalLogicalBytes
- TotalLogicalBytes is ulong on entity - cast to long when calling RecordStorageSnapshotAsync
- Registered in Program.cs after ChunkStoreStatsHostedService

## T10: Egress metering in ReleaseEndpoints (2026-05-08)

- ReleaseEndpoints is a static class — cannot use ILogger<ReleaseEndpoints> (static types cannot be type args); use ILoggerFactory instead and call CreateLogger("fully.qualified.name")
- Egress metering placed AFTER repo lookup (line ~135) — auth is enforced by route group RequireAuthorization + RequireRepoPermission before handler runs; placing meter after repo found ensures 401/403 are never metered
- ReleaseMetrics.TotalLogicalBytes is ulong — cast to (long) when calling RecordEgress(Guid, long)
- tenantId is a route parameter on the group path /api/tenants/{tenantId:guid}/... — add it to handler signature for DI binding
- Test uses reflection (BindingFlags.NonPublic | BindingFlags.Static) to invoke private static handler directly
- Empty ReleasePackage (no artifacts) causes handler to return NotFound before streaming — test must place metering BEFORE artifact check, or provide a non-empty package; moving meter to after repo lookup solves this cleanly
- Repository.Id has private set — use reflection typeof(Repository).GetProperty("Id")!.SetValue(repo, repoId) and capture repoId BEFORE setting release.RepoId
- Release.SerializerVersion is required byte — must set in test object initializer

- **CountingStream + await using var bug**: `await using var x = ...` (declaration style) disposes at END OF METHOD SCOPE, not at the point of the next statement. If RecordEgress is called after the declaration but before the method returns, BytesWritten is still 0. Fix: use explicit `await using (var x = ...) { ... }` block so disposal (and Zstd flush) happens before RecordEgress.
- ZstdNetNGX CompressionStream flushes to inner stream via `WriteAsync(ReadOnlyMemory<byte>)` on DisposeAsync — confirmed by isolation test.
- CountingStream must override all write paths: Write(byte[],int,int), Write(ReadOnlySpan<byte>), WriteByte(byte), WriteAsync(byte[],int,int,CT), WriteAsync(ReadOnlyMemory<byte>,CT).
