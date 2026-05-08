# BinStash SaaS Billing Plugin

## TL;DR

> **Quick Summary**: Wire a plugin-based billing layer into BinStash's OSS server (AGPLv3, dual-licensed) that adds Stripe usage metering for ingest bytes, egress bytes, and storage-at-rest — without embedding any Stripe code in the OSS codebase. A private `BinStash.SaaS` assembly is loaded at runtime, replaces the no-op billing provider via DI, and handles Stripe webhooks, tenant provisioning, and quota enforcement.
>
> **Deliverables**:
> - `BinStash.Core/Billing/` — `IBillingProvider`, `IUsageMeteringService`, `IBillingLimits`, `IBillingPluginLoader` interfaces + DTOs
> - `BinStash.Core/Billing/NoOp/` — `NoOpBillingProvider`, `NoOpUsageMeteringService`, `AddNoOpBilling()` DI extension
> - `BinStash.Server` — plugin loader infrastructure, ingest/egress meter call sites, quota enforcement at session-create, per-tenant storage stats job
> - `ReleaseEndpoints.cs` — hardcoded download password removed
> - `BinStash.Core/Entities/Subscription.cs` — deleted (clean break)
> - OSS side ships clean of Stripe; dual-license note added to LICENSE.txt
> - `BinStash.SaaS` private repo scaffold: `StripeBillingProvider`, `BillingDbContext`, webhook handler, signup endpoint, `checkout.session.completed` tenant provisioner, `invoice.created` minimum-fee top-up
>
> **Estimated Effort**: XL
> **Parallel Execution**: YES — 4 waves
> **Critical Path**: T1 (interfaces) → T3 (no-op) → T5 (ingest meter) + T6 (egress fix) → T8 (quota enforcement) → T10 (plugin loader) → T12 (SaaS provider) → T15 (webhooks) → Final Verification

---

## Context

### Original Request
Add SaaS billing to BinStash without open-sourcing the billing layer. Private assembly loaded at runtime, OSS defines interfaces + no-op stubs, three Stripe meters (ingest/egress/storage-at-rest), $10/month minimum, Stripe Checkout for signup, Stripe Customer Portal for account management.

### Interview Summary
**Key Discussions**:
- Plugin architecture: runtime assembly load via env var path. OSS zero Stripe references.
- Ingest meter: raw bytes including duplicates, fires after repo resolution in UploadChunks.
- Storage-at-rest: per-tenant logical size via `ReleaseMetrics.TotalLogicalBytes`, NOT per-store snapshots. New hosted job needed.
- Quota: hard 402 at session-create REST endpoint only. Cached 60s TTL.
- Tenant onboarding: Stripe Checkout → `checkout.session.completed` webhook → auto-provision tenant.
- EF migrations: **separate `BillingDbContext`** — two migration histories, OSS untouched.
- `Subscription` entity: **delete** from OSS Core. Plugin owns all billing state in `BillingProfile`.
- Plugin load failure: **hard crash** (fail to start). No silent revenue leaks.
- Ingest bytes: **all uploaded chunks including duplicates**.
- Quota enforcement: **session-create REST endpoint only**.
- License: **dual-license BinStash** (sole copyright holder). Add commercial license option.

**Research Findings**:
- Existing `Subscription` entity has BillingMode/SubscriptionStatus/MinimumMonthlyFee — will be deleted.
- `TenantPermission.BillingAdmin` already exists — all billing endpoints use it.
- `ReleaseEndpoints.cs:112` has hardcoded download password — security bug, fix before egress meter.
- `ChunkStoreStatsHostedService` is per-store, not per-tenant — need new per-tenant job.
- gRPC `UploadChunks` resolves `repo.TenantId` via DB at lines 225-227 — meter after this point.
- HotChocolate/EF Core DI: plugin must register services **before** `builder.Build()`.

### Metis Review
**Identified Gaps** (addressed):
- AGPL §13: resolved via dual-licensing decision.
- EF migration topology: separate BillingDbContext chosen.
- Subscription entity fate: delete chosen.
- Hardcoded password must be fixed before egress meter — sequenced in plan.
- Plugin DLL path: env var only, hard crash on load failure.
- Storage-at-rest metric: new per-tenant job instead of reusing per-store service.
- Quota check granularity: session-create only, 60s TTL cache.
- Webhook deduplication: `ProcessedWebhookEvent` table required.
- Metering hot-path: fire-and-forget with bounded channel, never block ingest.
- HotChocolate plugin load ordering: plugin loads before `builder.Build()`.

---

## Work Objectives

### Core Objective
Add a complete, legally clean SaaS billing plugin boundary to BinStash: OSS ships with no-op billing provider; the private `BinStash.SaaS` plugin provides Stripe-backed billing, quota enforcement, and tenant lifecycle management.

### Concrete Deliverables
- `BinStash.Core/Billing/IBillingProvider.cs` + related interfaces
- `BinStash.Core/Billing/NoOp/NoOpBillingProvider.cs` + DI extension
- `BinStash.Server/Billing/BillingPluginLoader.cs`
- Ingest meter call site in `IngestGrpcService.cs`
- Egress meter call site in `ReleaseEndpoints.cs` (after password fix)
- Quota 402 enforcement in ingest session create endpoint
- `BinStash.Server/HostedServices/TenantStorageStatsHostedService.cs`
- `BinStash.Core/Entities/Subscription.cs` deleted + EF migration removing the table
- `LICENSE-COMMERCIAL.md` added to repo root
- `BinStash.SaaS` private repo scaffold with full Stripe implementation

### Definition of Done
- [ ] `dotnet build BinStash.slnx` → 0 errors, 0 warnings (billing-related)
- [ ] `dotnet test BinStash.slnx` → all existing 341+ tests pass
- [ ] `Select-String -Path "BinStash.{Core,Contracts,Infrastructure,Server,Cli}/**/*.cs" -Pattern "Stripe"` → zero matches
- [ ] Server starts without `BillingPluginPath` set → healthy, NoOp provider active
- [ ] Server fails to start with invalid `BillingPluginPath` → explicit error, non-zero exit
- [ ] `Select-String -Path "BinStash.Server/Endpoints/ReleaseEndpoints.cs" -Pattern "D9BvHVpGlpaa9C8w230kQ8w8PIKUoc3k"` → zero matches
- [ ] Download endpoint without auth → HTTP 401

### Must Have
- Zero Stripe SDK references in OSS projects
- `IBillingProvider` and `IUsageMeteringService` in `BinStash.Core/Billing/`
- `NoOpBillingProvider` as default DI registration
- Metering calls fire-and-forget (bounded channel), never block hot path
- Plugin loaded before `builder.Build()` so it can register services
- Hard crash on plugin load failure when `BillingPluginPath` is set
- Fix hardcoded download password BEFORE egress meter task
- Separate `BillingDbContext` in plugin; two `Migrate()` calls at startup
- `ProcessedWebhookEvent` deduplication table in plugin DB
- Dual-license note in LICENSE + `LICENSE-COMMERCIAL.md`
- Per-tenant storage stats job (sum `ReleaseMetrics.TotalLogicalBytes` GROUP BY `Repository.TenantId`)
- `TenantPermission.BillingAdmin` used on all plugin billing endpoints

### Must NOT Have (Guardrails)
- No Stripe SDK / NuGet reference in `BinStash.Contracts`, `BinStash.Core`, `BinStash.Infrastructure`, `BinStash.Server`, `BinStash.Cli`
- No plugin route registration in OSS `Program.cs` or endpoint mapping files
- No `if (provider is StripeBillingProvider)` conditional branches in OSS
- No `BillingProfile` entity or `StripeCustomerId` fields in OSS EF context
- No custom billing UI (Stripe Customer Portal redirect only)
- No usage dashboard/charts in OSS
- No GraphQL types for billing data in HotChocolate schema
- No tax handling, coupons, annual billing, per-repo billing, plan tiers
- No quota check inside per-chunk loop in `UploadChunks`
- No plugin DLL path in `DbConfigurationSource` (env var only)
- No Stripe secrets stored in database

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit, 341 tests)
- **Automated tests**: Tests-after for new interfaces/no-op; TDD for quota enforcement and plugin loader
- **Framework**: xUnit (existing)
- **OSS side**: Add unit tests for `NoOpBillingProvider`, `BillingPluginLoader` (load/fail behavior)
- **SaaS side** (private repo): Integration tests with Stripe test-mode keys + webhook test sink

### QA Policy
Every task includes agent-executed QA scenarios. Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **Build/compile**: `dotnet build` assertion
- **Unit tests**: `dotnet test` with filter
- **API behavior**: `curl` against running server
- **Source hygiene**: `Select-String` / grep assertions
- **Integration**: Stripe CLI `stripe trigger` for webhook scenarios (SaaS tasks only)

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — OSS foundation, no dependencies):
├── T1: Billing interfaces + DTOs in BinStash.Core [quick]
├── T2: Delete Subscription entity + EF migration [quick]
├── T3: Fix hardcoded download password in ReleaseEndpoints.cs [quick]
└── T4: Dual-license update (LICENSE + LICENSE-COMMERCIAL.md) [writing]

Wave 2 (After T1 — OSS no-op + infrastructure):
├── T5: NoOpBillingProvider + DI extension (depends: T1) [quick]
├── T6: BillingPluginLoader infrastructure (depends: T1) [unspecified-high]
├── T7: Ingest meter call site in IngestGrpcService (depends: T1, T5) [unspecified-high]
└── T8: Per-tenant storage stats hosted service (depends: T1) [unspecified-high]

Wave 3 (After T2+T3+T5+T6+T7 — quota + egress):
├── T9: Quota enforcement at session-create (depends: T5, T6) [unspecified-high]
├── T10: Egress meter call site in ReleaseEndpoints (depends: T1, T3, T5) [unspecified-high]
└── T11: Wire plugin loader into Program.cs + startup tests (depends: T6, T9, T10) [unspecified-high]

Wave 4 (Private repo — BinStash.SaaS, after T11 OSS API is stable):
├── T12: BinStash.SaaS project scaffold + BillingDbContext + BillingProfile entity [unspecified-high]
├── T13: StripeBillingProvider + IUsageMeteringService Stripe implementation (depends: T12) [deep]
├── T14: Webhook handler + ProcessedWebhookEvent dedup + checkout.session.completed (depends: T12) [deep]
├── T15: invoice.created minimum-fee top-up webhook handler (depends: T14) [unspecified-high]
├── T16: /saas/signup endpoint + Stripe Checkout session create (depends: T12) [unspecified-high]
└── T17: /saas/portal endpoint (Stripe Customer Portal redirect, depends: T12) [quick]

Wave FINAL (After ALL tasks):
├── F1: Plan compliance audit + source hygiene (oracle)
├── F2: Code quality review + build/test (unspecified-high)
├── F3: Real E2E QA — no-op path + plugin path (unspecified-high)
└── F4: Scope fidelity check (deep)
→ Present results → Get user okay
```

### Agent Dispatch Summary
- **Wave 1**: 4 tasks — T1→`quick`, T2→`quick`, T3→`quick`, T4→`writing`
- **Wave 2**: 4 tasks — T5→`quick`, T6→`unspecified-high`, T7→`unspecified-high`, T8→`unspecified-high`
- **Wave 3**: 3 tasks — T9→`unspecified-high`, T10→`unspecified-high`, T11→`unspecified-high`
- **Wave 4**: 6 tasks — T12→`unspecified-high`, T13→`deep`, T14→`deep`, T15→`unspecified-high`, T16→`unspecified-high`, T17→`quick`
- **Final**: 4 tasks — F1→`oracle`, F2→`unspecified-high`, F3→`unspecified-high`, F4→`deep`

---

## TODOs

- [x] T1. **Billing interfaces + DTOs in BinStash.Core**

  **What to do**:
  - Create `BinStash.Core/Billing/` directory
  - Define `IBillingProvider.cs`:
    ```csharp
    Task<IBillingLimits> GetLimitsAsync(Guid tenantId, CancellationToken ct = default);
    ```
  - Define `IUsageMeteringService.cs`:
    ```csharp
    void RecordIngest(Guid tenantId, long bytes);  // fire-and-forget
    void RecordEgress(Guid tenantId, long bytes);   // fire-and-forget
    Task RecordStorageSnapshotAsync(Guid tenantId, long bytes, CancellationToken ct = default);
    ```
  - Define `IBillingLimits.cs`:
    ```csharp
    bool IsStorageAllowed { get; }
    bool IsIngestAllowed { get; }
    bool IsEgressAllowed { get; }
    long MaxStorageBytes { get; }  // long.MaxValue = unlimited
    ```
  - Define `IBillingPluginRegistrar.cs`:
    ```csharp
    void Register(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder app);
    ```
    `Register` is called before `builder.Build()` for DI registration; `MapEndpoints` is called after `app = builder.Build()` for route mapping — both invoked by the plugin loader in `Program.cs`.
  - Create `BinStash.Contracts/Billing/` with any shared DTOs needed across CLI/Server (keep minimal — only if CLI needs billing types; otherwise keep in Core)
  - Add copyright header (AGPLv3 notice, author: Lukas Eßmann) on all new files

  **Must NOT do**:
  - No Stripe types, no Stripe NuGet references
  - No infrastructure dependencies in Core
  - No `BillingProfile` entity (that's plugin-only)
  - Do not modify existing Core entities

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Pure interface definitions, no logic, no external deps
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 1
  - **Parallel Group**: Wave 1 (with T2, T3, T4)
  - **Blocks**: T5, T6, T7, T8, T9, T10, T12, T13, T14, T15, T16, T17
  - **Blocked By**: None

  **References**:
  - `BinStash.Core/Entities/` — naming conventions and file structure to follow
  - `BinStash.Core/Chunking/IChunker.cs` — interface style pattern
  - Copyright header pattern: any existing `.cs` file in Core

  **Acceptance Criteria**:
  - [ ] `dotnet build BinStash.Core/BinStash.Core.csproj` → 0 errors
  - [ ] Files exist: `BinStash.Core/Billing/IBillingProvider.cs`, `IUsageMeteringService.cs`, `IBillingLimits.cs`, `IBillingPluginRegistrar.cs`

  ```
  Scenario: Interfaces compile cleanly
    Tool: Bash
    Steps:
      1. dotnet build BinStash.Core/BinStash.Core.csproj --configuration Release
    Expected Result: "Build succeeded" with 0 errors
    Evidence: .sisyphus/evidence/task-1-build.txt

  Scenario: No Stripe references introduced
    Tool: Bash
    Steps:
      1. Select-String -Path "BinStash.Core/Billing" -Pattern "Stripe" -Recurse
    Expected Result: zero matches
    Evidence: .sisyphus/evidence/task-1-stripe-check.txt
  ```

  **Commit**: YES (with T2, T3, T4 as Wave 1 commit)
  - Message: `feat(billing): add core billing interfaces and DTOs`

---

- [x] T2. **Delete Subscription entity + EF migration**

  **What to do**:
  - Delete `BinStash.Core/Entities/Subscription.cs`
  - Remove `DbSet<Subscription>` from `BinStash.Infrastructure/Data/BinStashDbContext.cs`
  - Remove `IEntityTypeConfiguration<Subscription>` if it exists in `BinStash.Infrastructure/`
  - Remove any HotChocolate GraphQL type registration for `Subscription` in `BinStash.Server/GraphQL/`
  - Remove any service/repository referencing `Subscription` entity
  - Create EF Core migration: `dotnet ef migrations add RemoveSubscriptionEntity --project BinStash.Infrastructure --startup-project BinStash.Server`
  - Verify migration drops the `Subscriptions` table
  - Fix any compilation errors caused by removing the entity (may be zero)

  **Must NOT do**:
  - Do not create a replacement `BillingProfile` entity in OSS (plugin-only)
  - Do not rename to `BillingProfile` — delete cleanly

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Mechanical delete + migration generation
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 1
  - **Parallel Group**: Wave 1 (with T1, T3, T4)
  - **Blocks**: T11 (OSS must compile before wiring)
  - **Blocked By**: None

  **References**:
  - `BinStash.Core/Entities/Subscription.cs` — file to delete
  - `BinStash.Infrastructure/Data/BinStashDbContext.cs` — remove DbSet
  - `BinStash.Server/GraphQL/` — search for Subscription type registrations
  - Existing migration files under `BinStash.Infrastructure/Data/Migrations/` — follow naming convention

  **Acceptance Criteria**:
  - [ ] `Select-String -Path "BinStash.{Core,Infrastructure,Server}" -Pattern "class Subscription" -Recurse` → zero matches (excluding any unrelated `Subscription` like HotChocolate's built-in subscription type)
  - [ ] `dotnet build BinStash.slnx` → 0 errors
  - [ ] New migration file exists and contains `DropTable(name: "Subscriptions")` (or equivalent)
  - [ ] `dotnet test BinStash.slnx` → all existing tests pass

  ```
  Scenario: Solution compiles after entity deletion
    Tool: Bash
    Steps:
      1. dotnet build BinStash.slnx --configuration Release
    Expected Result: "Build succeeded" with 0 errors
    Evidence: .sisyphus/evidence/task-2-build.txt

  Scenario: No Subscription entity class remains
    Tool: Bash
    Steps:
      1. Select-String -Path "BinStash.Core","BinStash.Infrastructure","BinStash.Server" -Pattern "class Subscription\b" -Recurse
    Expected Result: zero matches (HotChocolate ISubscription is fine, check for "class Subscription")
    Evidence: .sisyphus/evidence/task-2-entity-check.txt
  ```

  **Commit**: YES (Wave 1 batch commit)

---

- [x] T3. **Fix hardcoded download password in ReleaseEndpoints.cs**

  **What to do**:
  - Open `BinStash.Server/Endpoints/ReleaseEndpoints.cs`, locate line ~112
  - Find the hardcoded string `"D9BvHVpGlpaa9C8w230kQ8w8PIKUoc3k"` used as a download password/token
  - Understand the auth flow: is this a bypass, a shared secret, or a legacy dev artifact?
  - Replace with proper auth: the download endpoint must require the composite Smart auth scheme (JWT Bearer / ApiKey / Cookie) and return 401 if unauthenticated
  - Add `[Authorize]` or equivalent minimal-API `.RequireAuthorization()` to the download endpoint if missing
  - Verify the endpoint returns HTTP 401 when called without credentials
  - Verify authenticated download still works (use an existing test fixture or manual curl with valid token)

  **Must NOT do**:
  - Do not refactor the entire `GetReleaseDownloadAsync` method — only fix the auth gap
  - Do not change the download logic, streaming behavior, or response format

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Targeted single-file security fix
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 1
  - **Parallel Group**: Wave 1 (with T1, T2, T4)
  - **Blocks**: T10 (egress meter cannot be wired until auth is clean)
  - **Blocked By**: None

  **References**:
  - `BinStash.Server/Endpoints/ReleaseEndpoints.cs:112` — the hardcoded password
  - `BinStash.Server/Endpoints/` — other endpoints for `.RequireAuthorization()` pattern
  - `BinStash.Server/Program.cs` — auth scheme registration, Smart scheme name

  **Acceptance Criteria**:
  - [ ] `Select-String -Path "BinStash.Server/Endpoints/ReleaseEndpoints.cs" -Pattern "D9BvHVpGlpaa9C8w230kQ8w8PIKUoc3k"` → zero matches
  - [ ] `dotnet build BinStash.Server` → 0 errors

  ```
  Scenario: Unauthenticated download returns 401
    Tool: Bash (curl)
    Preconditions: Server running locally on http://localhost:5000
    Steps:
      1. curl -i -s http://localhost:5000/api/tenants/00000000-0000-0000-0000-000000000001/repositories/00000000-0000-0000-0000-000000000001/releases/00000000-0000-0000-0000-000000000001/download
    Expected Result: HTTP/1.1 401 Unauthorized
    Failure Indicators: HTTP 200, HTTP 403, or any response body with file content
    Evidence: .sisyphus/evidence/task-3-auth-check.txt

  Scenario: Hardcoded password string is gone
    Tool: Bash
    Steps:
      1. Select-String -Path "BinStash.Server/Endpoints/ReleaseEndpoints.cs" -Pattern "D9BvHVpGlpaa9C8w230kQ8w8PIKUoc3k"
    Expected Result: zero matches (no output)
    Evidence: .sisyphus/evidence/task-3-password-check.txt
  ```

  **Commit**: YES (Wave 1 batch commit)

---

- [x] T4. **Dual-license update**

  **What to do**:
  - Add `LICENSE-COMMERCIAL.md` to repo root with a placeholder commercial license notice: "BinStash is available under the AGPLv3 for open-source use. For commercial/SaaS use without AGPLv3 obligations, contact [email]. Copyright © Lukas Eßmann."
  - Update repo root `LICENSE` (or `LICENSE.txt`) to add a note at the top: "This software is dual-licensed. See LICENSE-COMMERCIAL.md for commercial license options."
  - Update `README.md` to add a "License" section noting the dual-license
  - Do NOT change the AGPLv3 license text itself — only add the dual-license note

  **Must NOT do**:
  - Do not remove AGPLv3 text
  - Do not add specific pricing or terms (placeholder only)

  **Recommended Agent Profile**:
  - **Category**: `writing`
    - Reason: Pure documentation/legal text authoring
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 1
  - **Parallel Group**: Wave 1 (with T1, T2, T3)
  - **Blocks**: nothing
  - **Blocked By**: None

  **References**:
  - Repo root `LICENSE.txt` — existing AGPL text
  - Example dual-license preamble: HashiCorp (BSL), Sentry (FSL) for structure reference (agent can look these up)

  **Acceptance Criteria**:
  - [ ] `LICENSE-COMMERCIAL.md` exists at repo root
  - [ ] `LICENSE.txt` contains "dual-licensed" or "commercial license" in first 10 lines

  ```
  Scenario: Commercial license file exists
    Tool: Bash
    Steps:
      1. Test-Path -LiteralPath "LICENSE-COMMERCIAL.md"
    Expected Result: True
    Evidence: .sisyphus/evidence/task-4-license-check.txt
  ```

  **Commit**: YES (Wave 1 batch commit)

---

- [x] T5. **NoOpBillingProvider + DI extension**

  **What to do**:
  - Create `BinStash.Core/Billing/NoOp/NoOpBillingProvider.cs` implementing `IBillingProvider`:
    - `GetLimitsAsync` returns `NoOpBillingLimits` (all `IsXAllowed = true`, `MaxStorageBytes = long.MaxValue`)
  - Create `BinStash.Core/Billing/NoOp/NoOpUsageMeteringService.cs` implementing `IUsageMeteringService`:
    - All methods are no-ops (log at Debug level only)
  - Create `BinStash.Core/Billing/NoOp/NoOpBillingLimits.cs` implementing `IBillingLimits`
  - Create `BinStash.Server/Billing/BillingServiceCollectionExtensions.cs`:
    ```csharp
    public static IServiceCollection AddNoOpBilling(this IServiceCollection services)
    {
        services.AddSingleton<IBillingProvider, NoOpBillingProvider>();
        services.AddSingleton<IUsageMeteringService, NoOpUsageMeteringService>();
        return services;
    }
    ```
  - Wire `services.AddNoOpBilling()` in `BinStash.Server/Program.cs` (before plugin loader call)
  - Add xUnit unit test: `BillingDiTests.cs` — verifies `IBillingProvider` resolves to `NoOpBillingProvider` when no plugin is loaded

  **Must NOT do**:
  - Do not use `TryAddSingleton` — use explicit `AddSingleton` so plugin can `Replace` it cleanly
  - No Stripe references

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Simple no-op implementations, DI extension, one test
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 2
  - **Parallel Group**: Wave 2 (with T6, T7, T8)
  - **Blocks**: T9, T10, T11
  - **Blocked By**: T1

  **References**:
  - `BinStash.Core/Billing/IBillingProvider.cs` (T1 output)
  - `BinStash.Server/Program.cs` — DI registration order (add after existing services, before plugin loader)
  - `BinStash.Core.Tests/` — test file naming and xUnit patterns

  **Acceptance Criteria**:
  - [ ] `dotnet build BinStash.slnx` → 0 errors
  - [ ] `dotnet test --filter "FullyQualifiedName~BillingDi"` → 1 test passes

  ```
  Scenario: NoOp provider allows all operations
    Tool: Bash
    Steps:
      1. dotnet test BinStash.Core.Tests --filter "FullyQualifiedName~BillingDi" --logger "console;verbosity=detailed"
    Expected Result: 1 test passed, 0 failed
    Evidence: .sisyphus/evidence/task-5-nooptest.txt
  ```

  **Commit**: YES (Wave 2 batch)

---

- [x] T6. **BillingPluginLoader infrastructure**

  **What to do**:
  - Create `BinStash.Server/Billing/BillingPluginLoader.cs`:
    - Reads `BillingPluginPath` from `IConfiguration` (env var: `BINSTASH_BILLING_PLUGIN_PATH`)
    - If null/empty: logs "No billing plugin configured, using no-op provider" and returns (no-op already registered)
    - If set: attempts `Assembly.LoadFrom(path)`
    - On load failure: throws `InvalidOperationException("Failed to load billing plugin from {path}: {ex.Message}")` — causes startup crash
    - Looks for a type implementing `IBillingPluginRegistrar` in the loaded assembly
    - Calls `registrar.Register(services, configuration)` — plugin registers its own services using `services.Replace<IBillingProvider>(...)` etc.
    - Must be called in `Program.cs` **before** `builder.Build()`
    - Store the `IBillingPluginRegistrar` instance; expose it for a second call to `registrar.MapEndpoints(app)` after `app = builder.Build()` so plugin routes are registered on the live `IEndpointRouteBuilder`
    - `BillingPluginLoader` exposes two public entry points: `LoadAndRegisterServices(WebApplicationBuilder builder)` and `MapPluginEndpoints(WebApplication app)`
  - Add startup validation: if `BillingPluginPath` is set, and `IBillingProvider` post-Build is still `NoOpBillingProvider`, log a warning (plugin may not have replaced it)
  - Add unit tests: `BillingPluginLoaderTests.cs`
    - `NullPath_RegistersNoOp`: no path → builder has NoOp
    - `InvalidPath_ThrowsOnLoad`: bad path → `InvalidOperationException`
    - `ValidPlugin_CallsRegistrar`: mock assembly with `IBillingPluginRegistrar` → registrar called

  **Must NOT do**:
  - Do not allow `BillingPluginPath` to come from `DbConfigurationSource` — document this explicitly in the code with a comment
  - Do not load into a separate `AssemblyLoadContext` (simplicity; plugin is trusted)
  - Do not catch and swallow the load exception

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Assembly loading, DI interop, edge cases (load failure, missing registrar type), unit tests required
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 2
  - **Parallel Group**: Wave 2 (with T5, T7, T8)
  - **Blocks**: T9, T11, T12
  - **Blocked By**: T1

  **References**:
  - `BinStash.Core/Billing/IBillingPluginRegistrar.cs` (T1 output)
  - `BinStash.Server/Program.cs` — startup wiring location
  - `BinStash.Core.Tests/` — xUnit test patterns

  **Acceptance Criteria**:
  - [ ] `dotnet test --filter "FullyQualifiedName~BillingPluginLoader"` → 3 tests pass
  - [ ] `BINSTASH_BILLING_PLUGIN_PATH=C:\nonexistent.dll dotnet run --project BinStash.Server` → exits non-zero with error message containing "Failed to load billing plugin"

  ```
  Scenario: Invalid plugin path causes startup failure
    Tool: Bash
    Preconditions: BinStash.Server built in Release
    Steps:
      1. $env:BINSTASH_BILLING_PLUGIN_PATH = "C:\nonexistent\BogusPlugin.dll"
      2. dotnet run --project BinStash.Server -- 2>&1 | Select-Object -First 20
    Expected Result: Process exits with non-zero exit code; output contains "Failed to load billing plugin"
    Failure Indicators: Server starts successfully, or exits without error message
    Evidence: .sisyphus/evidence/task-6-plugin-crash.txt

  Scenario: No plugin path configured → server process starts successfully
    Tool: Bash
    Steps:
      1. Remove-Item Env:BINSTASH_BILLING_PLUGIN_PATH -ErrorAction SilentlyContinue
      2. $proc = Start-Process dotnet -ArgumentList "run --project BinStash.Server --urls=http://localhost:5777" -PassThru -NoNewWindow; Start-Sleep 8
      3. Assert $proc.HasExited -eq $false (process still running, no crash)
      4. Stop-Process -Id $proc.Id
    Expected Result: Process still running after 8 seconds (no crash on startup)
    Evidence: .sisyphus/evidence/task-6-noplugin-starts.txt
  ```

  **Commit**: YES (Wave 2 batch)

---

- [x] T7. **Ingest meter call site in IngestGrpcService**

  **What to do**:
  - Open `BinStash.Server/Grpc/IngestGrpcService.cs`
  - Inject `IUsageMeteringService` via constructor
  - In `UploadChunks`: after `repo.TenantId` is resolved (lines ~225-227), add metering call for each chunk:
    ```csharp
    _meteringSvc.RecordIngest(repo.TenantId, request.Data.Length);
    ```
  - The call is **synchronous** on the interface but implementation is fire-and-forget via bounded channel — the interface method is void, not async, for this reason
  - Wrap in `try/catch` logging `ILogger` at Warning on failure — never throw from metering
  - Comment in code: `// Billing: meter fires for ALL chunks including duplicates (per pricing policy)`
  - Add unit test asserting `RecordIngest` is called with correct tenantId and byte count for a successful chunk upload

  **Must NOT do**:
  - Do not call `RecordIngest` before `repo.TenantId` is resolved
  - Do not call inside the per-chunk dedup early-exit path if that would bill before resolution — verify exact insertion point
  - Do not block the gRPC hot path (method is void, not awaited)
  - Do not add quota check here (T9 handles that at session-create)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Threading correctness (fire-and-forget semantics), hot-path safety, test required
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 2
  - **Parallel Group**: Wave 2 (with T5, T6, T8)
  - **Blocks**: T11
  - **Blocked By**: T1, T5

  **References**:
  - `BinStash.Server/Grpc/IngestGrpcService.cs:225-227` — repo.TenantId resolution point
  - `BinStash.Core/Billing/IUsageMeteringService.cs` (T1 output)
  - `BinStash.Server/Grpc/IngestGrpcService.cs` — existing constructor injection pattern

  **Acceptance Criteria**:
  - [ ] `dotnet build BinStash.slnx` → 0 errors
  - [ ] Unit test: `RecordIngest` called with correct tenantId and `request.Data.Length`

  ```
  Scenario: Metering called after repo resolution
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~IngestMeter" --logger "console;verbosity=detailed"
    Expected Result: test passes; mock IUsageMeteringService received RecordIngest(tenantId, expectedBytes)
    Evidence: .sisyphus/evidence/task-7-meter-test.txt
  ```

  **Commit**: YES (Wave 2 batch)

---

- [x] T8. **Per-tenant storage stats hosted service**

  **What to do**:
  - Create `BinStash.Server/HostedServices/TenantStorageStatsHostedService.cs`
  - Implements `BackgroundService`; runs every 1 hour (configurable via `appsettings.json` key `Billing:StorageStatsIntervalMinutes`, default 60)
  - Query: `SELECT r.TenantId, SUM(rm.TotalLogicalBytes) FROM Releases rel JOIN ReleaseMetrics rm ON rel.Id = rm.ReleaseId JOIN Repositories r ON rel.RepositoryId = r.Id GROUP BY r.TenantId`
  - For each tenant, call `IUsageMeteringService.RecordStorageSnapshotAsync(tenantId, totalBytes, ct)`
  - Use scoped `IServiceScopeFactory` for EF Core access (hosted service is singleton)
  - Register in `Program.cs` via `services.AddHostedService<TenantStorageStatsHostedService>()`
  - Do NOT modify `ChunkStoreStatsHostedService` — this is a new separate service
  - Add unit test mocking the DB query result and verifying `RecordStorageSnapshotAsync` called per tenant

  **Must NOT do**:
  - Do not touch `ChunkStoreStatsHostedService`
  - Do not join to `ChunkStore` table — storage-at-rest is per-tenant logical size, not per-store
  - Do not run more than once per configurable interval (avoid hammering DB)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: BackgroundService pattern, EF Core scoped factory, LINQ aggregation query
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 2
  - **Parallel Group**: Wave 2 (with T5, T6, T7)
  - **Blocks**: T11
  - **Blocked By**: T1

  **References**:
  - `BinStash.Server/HostedServices/ChunkStoreStatsHostedService.cs` — existing pattern for BackgroundService + scoped factory
  - `BinStash.Infrastructure/Data/BinStashDbContext.cs` — `Releases`, `ReleaseMetrics`, `Repositories` DbSets
  - `BinStash.Core/Billing/IUsageMeteringService.cs` (T1 output)

  **Acceptance Criteria**:
  - [ ] `dotnet build BinStash.slnx` → 0 errors
  - [ ] Unit test: mock with 2 tenants → `RecordStorageSnapshotAsync` called twice with correct byte sums

  ```
  Scenario: Per-tenant storage aggregation fires correctly
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~TenantStorageStats" --logger "console;verbosity=detailed"
    Expected Result: 1 test passed with correct per-tenant byte sums
    Evidence: .sisyphus/evidence/task-8-storage-test.txt
  ```

  **Commit**: YES (Wave 2 batch)

---

- [x] T9. **Quota enforcement at session-create**

  **What to do**:
  - Find the ingest session create REST endpoint in `BinStash.Server/Endpoints/` (likely `IngestSessionEndpoints.cs` or similar — search for POST endpoint that creates an `IngestSession`)
  - Inject `IBillingProvider` into the endpoint handler
  - Before creating the session, call `await _billingProvider.GetLimitsAsync(tenantId)`
  - If `!limits.IsIngestAllowed`, return `Results.Problem(statusCode: 402, title: "Quota exceeded", detail: "Your plan does not allow further ingest.")`
  - Cache the limits: inject `IMemoryCache` + use `GetOrCreateAsync` with 60s TTL keyed by `tenantId`
  - Create `BinStash.Server/Billing/BillingLimitsCache.cs` as a thin wrapper: `Task<IBillingLimits> GetCachedLimitsAsync(Guid tenantId)`
  - Add unit test: `QuotaEnforcementTests.cs` — mock `IBillingProvider` returning `IsIngestAllowed = false` → endpoint returns 402

  **Must NOT do**:
  - Do not add quota check inside `UploadChunks` gRPC per-chunk loop
  - Do not call `GetLimitsAsync` without caching (would hit Stripe/DB on every session create)
  - Do not use sliding expiration — absolute 60s TTL

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Caching layer, 402 response shape, unit test with mock
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 3
  - **Parallel Group**: Wave 3 (with T10, T11)
  - **Blocks**: T11
  - **Blocked By**: T5, T6

  **References**:
  - `BinStash.Server/Endpoints/` — search for `IngestSession` POST endpoint
  - `BinStash.Core/Billing/IBillingProvider.cs` (T1 output)
  - `BinStash.Server/` — existing `IMemoryCache` usage patterns (search for `IMemoryCache`)
  - `Results.Problem` usage in other endpoints for 402 response shape

  **Acceptance Criteria**:
  - [ ] `dotnet test --filter "FullyQualifiedName~QuotaEnforcement"` → test passes
  - [ ] Manual: POST ingest session with mocked quota-exceeded provider → HTTP 402

  ```
  Scenario: Quota exceeded returns 402
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~QuotaEnforcement" --logger "console;verbosity=detailed"
    Expected Result: 1 test passed; 402 status code returned when IsIngestAllowed = false
    Evidence: .sisyphus/evidence/task-9-quota-test.txt
  ```

  **Commit**: YES (Wave 3 batch)

---

- [x] T10. **Egress meter call site in ReleaseEndpoints**

  **What to do**:
  - Open `BinStash.Server/Endpoints/ReleaseEndpoints.cs`
  - Inject `IUsageMeteringService` via endpoint handler parameters or DI
  - After auth succeeds and before streaming the release bytes, add:
    ```csharp
    _meteringSvc.RecordEgress(tenantId, release.TotalBytes); // or equivalent size field
    ```
  - Identify correct size field: likely `ReleaseMetrics.TotalLogicalBytes` or response content-length — document which field is used in a code comment
  - Wrap in `try/catch` logging at Warning — never throw from metering
  - Enumerate which exact endpoints are metered: document as a comment block at the top of the file. Meter: release download endpoint. DO NOT meter: health, OpenAPI, GraphQL responses.
  - Add unit test verifying `RecordEgress` called on successful download

  **Must NOT do**:
  - Do not add egress meter before auth check (auth fix from T3 must be in place)
  - Do not meter failed/unauthorized requests
  - Do not meter GraphQL or health endpoints

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Must identify correct size field, threading safety, test required
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 3
  - **Parallel Group**: Wave 3 (with T9, T11)
  - **Blocks**: T11
  - **Blocked By**: T1, T3 (password fix must be done first), T5

  **References**:
  - `BinStash.Server/Endpoints/ReleaseEndpoints.cs` — post-T3 state
  - `BinStash.Core/Billing/IUsageMeteringService.cs` (T1 output)
  - `BinStash.Infrastructure/Data/` — `ReleaseMetrics` entity for size field

  **Acceptance Criteria**:
  - [ ] `dotnet build BinStash.slnx` → 0 errors
  - [ ] Unit test: `RecordEgress` called with correct bytes on successful download
  - [ ] `RecordEgress` NOT called on 401 response

  ```
  Scenario: Egress metered on successful download
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~EgressMeter" --logger "console;verbosity=detailed"
    Expected Result: test passed; RecordEgress called with expected bytes
    Evidence: .sisyphus/evidence/task-10-egress-test.txt
  ```

  **Commit**: YES (Wave 3 batch)

---

- [ ] T11. **Wire plugin loader into Program.cs + integration smoke tests**

  **What to do**:
  - In `BinStash.Server/Program.cs`, after `services.AddNoOpBilling()`, call the plugin loader for service registration:
    ```csharp
    var billingLoader = new BillingPluginLoader(builder.Configuration, logger);
    billingLoader.LoadAndRegisterServices(builder.Services);
    ```
  - After `var app = builder.Build()`, call the plugin loader for route mapping:
    ```csharp
    billingLoader.MapPluginEndpoints(app);
    ```
  - Ensure the first call runs **before** `builder.Build()` so plugin can register services; second call after `builder.Build()` for endpoint mapping
  - Ensure `TenantStorageStatsHostedService` is registered
  - Run full solution build and all existing tests
  - Create an integration smoke test `BillingStartupTests.cs`:
    - Test 1: `NoPluginPath_ServerBuildsSuccessfully` — build WebApplicationFactory with no `BillingPluginPath`, assert `WebApplicationFactory.CreateClient()` succeeds without throwing (i.e., startup completes; no auth-guarded endpoint needed)
    - Test 2: `InvalidPluginPath_ThrowsOnBuild` — set `BillingPluginPath` to nonexistent path → `WebApplicationFactory` throws on build

  **Must NOT do**:
  - Do not call plugin loader after `builder.Build()`
  - Do not add any Stripe-specific code

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Integration test with WebApplicationFactory, startup ordering critical
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO — Wave 3 final
  - **Parallel Group**: Wave 3 (sequential after T9, T10)
  - **Blocks**: T12 (OSS interface is now stable)
  - **Blocked By**: T2, T5, T6, T7, T8, T9, T10

  **References**:
  - `BinStash.Server/Program.cs` — existing service registration order
  - `BinStash.Server/Billing/BillingPluginLoader.cs` (T6 output)
  - Existing integration test setup in `BinStash.Core.Tests/` or server test project

  **Acceptance Criteria**:
  - [ ] `dotnet test BinStash.slnx` → all 341+ existing tests pass + 2 new smoke tests pass
  - [ ] `dotnet build BinStash.slnx` → 0 errors

  ```
  Scenario: Full solution tests pass after wiring
    Tool: Bash
    Steps:
      1. dotnet test BinStash.slnx --configuration Release --logger "console;verbosity=normal"
    Expected Result: All 343+ tests passed, 0 failed
    Evidence: .sisyphus/evidence/task-11-alltest.txt
  ```

  **Commit**: YES — `feat(billing): wire plugin loader and complete OSS billing boundary`

---

- [ ] T12. **BinStash.SaaS project scaffold + BillingDbContext + BillingProfile entity**

  **What to do**:
  - Create new project `BinStash.SaaS/BinStash.SaaS.csproj` in the `BinStash.SaaS` private repo (separate git repo)
  - Target `net10.0`, reference `BinStash.Core` and `BinStash.Contracts` (as project references or NuGet packages depending on build setup — document the choice)
  - Add NuGet references: `Stripe.net`, `Npgsql.EntityFrameworkCore.PostgreSQL 10`, `Microsoft.EntityFrameworkCore.Design`
  - Create `BillingDbContext.cs`:
    - Connection string: same `BinStashDb` connection from `IConfiguration["ConnectionStrings:BinStashDb"]`
    - Entities: `BillingProfile`, `ProcessedWebhookEvent`
    - Schema prefix: use `billing.` schema or table prefix `Billing_` to avoid collision with OSS tables
  - Create `BillingProfile.cs` entity:
    ```csharp
    Guid Id, Guid TenantId (plain Guid, no EF FK to Tenant), string StripeCustomerId, string StripeSubscriptionId, DateTime CreatedAt, DateTime? SuspendedAt
    ```
  - Create `ProcessedWebhookEvent.cs` entity:
    ```csharp
    string StripeEventId (PK), DateTime ProcessedAt
    ```
  - Generate first EF migration: `dotnet ef migrations add InitialBilling --project BinStash.SaaS --startup-project BinStash.SaaS`
  - Create `SaasPluginRegistrar.cs` implementing `IBillingPluginRegistrar`:
    - `Register(IServiceCollection, IConfiguration)`: registers `StripeBillingProvider` (T13), `StripeUsageMeteringService` (T13), `BillingDbContext`, webhook services
    - `MapEndpoints(IEndpointRouteBuilder app)`: maps plugin-only routes (`POST /webhooks/stripe`, `POST /saas/signup`, `POST /saas/portal`) — called after `builder.Build()`
  - Add `Migrate()` call for `BillingDbContext` alongside the existing OSS `db.Database.Migrate()` — this must be done by the plugin's startup hook (called from `MapEndpoints` or a post-build startup hook in `SaasPluginRegistrar`)

  **Must NOT do**:
  - Do not reference `BinStash.Infrastructure` from `BinStash.SaaS` for billing-specific entities (avoid polluting OSS migration history). `BinStash.SaaS` **may** reference `BinStash.Infrastructure` solely to obtain `BinStashDbContext` for OSS entity writes (tenant/user provisioning). Do not add plugin-owned entities to `BinStashDbContext`.
  - Do not put `BillingProfile` in `BinStashDbContext`
  - Do not store Stripe API keys in DB — use `IConfiguration` env var only

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: New project setup, dual-context EF, migration generation, plugin registrar pattern
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 4
  - **Parallel Group**: Wave 4 (with T13-T17, but T13-T17 depend on T12)
  - **Blocks**: T13, T14, T15, T16, T17
  - **Blocked By**: T11 (OSS interfaces must be stable)

  **References**:
  - `BinStash.Core/Billing/IBillingPluginRegistrar.cs` (T1 output)
  - `BinStash.Infrastructure/Data/BinStashDbContext.cs` — DbContext pattern to follow
  - `BinStash.Infrastructure/Data/Migrations/` — migration naming convention
  - `BinStash.Server/Program.cs` — where second `Migrate()` call will be invoked

  **Acceptance Criteria**:
  - [ ] `dotnet build BinStash.SaaS` → 0 errors
  - [ ] Migration file exists with `CreateTable` for `BillingProfile` and `ProcessedWebhookEvent`
  - [ ] `SaasPluginRegistrar` implements `IBillingPluginRegistrar`

  ```
  Scenario: SaaS project builds clean
    Tool: Bash
    Steps:
      1. dotnet build BinStash.SaaS/BinStash.SaaS.csproj --configuration Release
    Expected Result: "Build succeeded", 0 errors
    Evidence: .sisyphus/evidence/task-12-build.txt
  ```

  **Commit**: YES — `feat(saas): scaffold BinStash.SaaS project with BillingDbContext`

---

- [ ] T13. **StripeBillingProvider + StripeUsageMeteringService**

  **What to do**:
  - Create `BinStash.SaaS/Billing/StripeBillingProvider.cs` implementing `IBillingProvider`:
    - `GetLimitsAsync(tenantId)`: look up `BillingProfile` by `TenantId`, check `SuspendedAt` → if not null, return limits with `IsIngestAllowed = false`; otherwise return unlimited limits (quota is enforced externally via Stripe subscription status, not by hard byte caps in v1)
  - Create `BinStash.SaaS/Billing/StripeUsageMeteringService.cs` implementing `IUsageMeteringService`:
    - Internally: `Channel<UsageEvent>` bounded to 10,000 entries; background `Task` drains the channel and calls Stripe Meter Events API
    - `RecordIngest(tenantId, bytes)`: enqueue to channel (if full, log Warning + drop — never block)
    - `RecordEgress(tenantId, bytes)`: enqueue to channel
    - `RecordStorageSnapshotAsync(tenantId, bytes)`: directly call Stripe Meter Events API (not hot path)
    - Background drainer: batches events, calls `Stripe.MeterEventService.Create()` with meter name, customer ID (from `BillingProfile`), timestamp, value
    - Stripe API key: `IConfiguration["Stripe:SecretKey"]` (env var: `STRIPE_SECRET_KEY`)
    - Three meter names: `IConfiguration["Stripe:Meters:Ingest"]`, `"Stripe:Meters:Egress"`, `"Stripe:Meters:StorageAtRest"`
  - Add unit tests: bounded channel drops when full; `RecordIngest` enqueues event; `GetLimitsAsync` returns correct limits for suspended/active profile

  **Must NOT do**:
  - Do not block the calling thread in `RecordIngest`/`RecordEgress`
  - Do not store Stripe API keys anywhere except IConfiguration
  - Do not call Stripe in `RecordIngest`/`RecordEgress` synchronously

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Channel concurrency, Stripe SDK integration, bounded buffer semantics, multiple test cases
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 4
  - **Parallel Group**: Wave 4 (T13 + T14 can run in parallel after T12)
  - **Blocks**: F1-F4 (needs T13 for complete E2E)
  - **Blocked By**: T12

  **References**:
  - `BinStash.Core/Billing/IBillingProvider.cs`, `IUsageMeteringService.cs` (T1 outputs)
  - Stripe Meter Events API docs: https://docs.stripe.com/billing/subscriptions/usage-based
  - `BinStash.SaaS/Data/BillingDbContext.cs` (T12 output)

  **Acceptance Criteria**:
  - [ ] `dotnet test BinStash.SaaS.Tests --filter "FullyQualifiedName~StripeBilling"` → all tests pass
  - [ ] Channel drop: if channel full, method returns without blocking
  - [ ] Suspended tenant: `GetLimitsAsync` returns `IsIngestAllowed = false`

  ```
  Scenario: Metering service does not block on full channel
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~BoundedChannel" --logger "console;verbosity=detailed"
    Expected Result: test passed in < 100ms (no blocking)
    Evidence: .sisyphus/evidence/task-13-channel-test.txt
  ```

  **Commit**: YES — `feat(saas): Stripe billing provider and usage metering service`

---

- [ ] T14. **Webhook handler + ProcessedWebhookEvent dedup + checkout.session.completed**

  **What to do**:
  - Create `BinStash.SaaS/Webhooks/StripeWebhookHandler.cs`
  - Map endpoint `POST /webhooks/stripe` — registered only by the plugin (never in OSS `Program.cs`)
  - Verify Stripe-Signature header: `EventUtility.ConstructEvent(body, sig, webhookSecret)` where `webhookSecret = IConfiguration["Stripe:WebhookSecret"]`
  - On signature failure: return HTTP 400
  - Deduplication: check `ProcessedWebhookEvent` table for `event.Id`; if exists, return HTTP 200 (idempotent)
  - Handle `checkout.session.completed`:
    - Extract `customer`, `subscription` from session object
    - Look up or create `BillingProfile` for the tenant (tenant ID in session `metadata.tenantId`)
    - If tenant doesn't exist yet: call `TenantProvisioningService.ProvisionAsync(stripeCustomerId, email, tenantName)` which creates Tenant + BinStashUser (admin) in the OSS DB
    - Set `BillingProfile.StripeCustomerId`, `StripeSubscriptionId`
  - Create `TenantProvisioningService.cs` using `BinStashDbContext` (OSS context via DI) to create Tenant + user
  - Insert `ProcessedWebhookEvent` record after handling
  - Add tests: signature failure → 400; duplicate event → 200 no-op; valid `checkout.session.completed` → tenant provisioned

  **Must NOT do**:
  - Do not process webhook without signature verification
  - Do not process duplicate event IDs
  - Do not expose `/webhooks/stripe` in OSS server routing

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Stripe webhook verification, idempotency table, tenant provisioning, multiple event types, multiple tests
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 4 (parallel with T13)
  - **Parallel Group**: Wave 4 (T13 + T14 parallel)
  - **Blocks**: T15
  - **Blocked By**: T12

  **References**:
  - `BinStash.SaaS/Data/BillingDbContext.cs` (T12 output)
  - Stripe webhook verification docs: https://docs.stripe.com/webhooks/signatures
  - `BinStash.Infrastructure/Data/BinStashDbContext.cs` — Tenant/BinStashUser entities for provisioning
  - `TenantPermission.BillingAdmin` in `BinStash.Server/` — auth policy used on billing endpoints

  **Acceptance Criteria**:
  - [ ] Invalid signature → HTTP 400
  - [ ] Duplicate event ID → HTTP 200, no duplicate processing
  - [ ] `checkout.session.completed` → `BillingProfile` created, tenant provisioned

  ```
  Scenario: Forged webhook rejected
    Tool: Bash (curl)
    Steps:
      1. curl -X POST http://localhost:5000/webhooks/stripe -H "Stripe-Signature: t=0,v1=badhash" -d '{}'
    Expected Result: HTTP 400
    Evidence: .sisyphus/evidence/task-14-webhook-reject.txt

  Scenario: Duplicate event idempotent
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~WebhookDedup" --logger "console;verbosity=detailed"
    Expected Result: test passed; second call with same event.Id → 200, no new BillingProfile created
    Evidence: .sisyphus/evidence/task-14-webhook-dedup.txt
  ```

  **Commit**: YES — `feat(saas): Stripe webhook handler with deduplication`

---

- [ ] T15. **invoice.created minimum-fee top-up webhook handler**

  **What to do**:
  - In `StripeWebhookHandler.cs`, add handler for `invoice.created` event:
    - Retrieve the invoice object from the event
    - Get the current invoice amount (in cents): `invoice.AmountDue`
    - If `amountDue < minimumFeeInCents` (hardcoded as 1000 = $10.00, or configurable via `IConfiguration["Stripe:MinimumMonthlyFeeCents"]` default 1000):
      - Call Stripe Invoice Items API to add a line item for the difference: `Stripe.InvoiceItemService.Create()` with `amount = minimumFeeInCents - amountDue`, `currency = "usd"`, `customer = invoice.CustomerId`, `invoice = invoice.Id`, `description = "BinStash minimum monthly fee adjustment"`
    - Insert `ProcessedWebhookEvent` record
  - Add unit test: invoice with $3 usage → $7 top-up item created; invoice with $15 usage → no top-up

  **Must NOT do**:
  - Do not finalize the invoice (Stripe handles that)
  - Do not add top-up if invoice already meets minimum

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Stripe Invoice Items API, conditional logic, two test cases
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO — Wave 4 (depends on T14 webhook infrastructure)
  - **Parallel Group**: Wave 4 sequential after T14
  - **Blocks**: F1-F4
  - **Blocked By**: T14

  **References**:
  - `BinStash.SaaS/Webhooks/StripeWebhookHandler.cs` (T14 output)
  - Stripe Invoice Items API: https://docs.stripe.com/api/invoiceitems/create
  - `IConfiguration["Stripe:MinimumMonthlyFeeCents"]` — configurable minimum

  **Acceptance Criteria**:
  - [ ] `dotnet test --filter "FullyQualifiedName~MinimumFee"` → both tests pass
  - [ ] `amountDue < 1000`: top-up item created for difference
  - [ ] `amountDue >= 1000`: no top-up item created

  ```
  Scenario: Low-usage invoice gets top-up
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~MinimumFeeTopUp" --logger "console;verbosity=detailed"
    Expected Result: both tests pass (low usage → top-up; high usage → no top-up)
    Evidence: .sisyphus/evidence/task-15-minimum-fee-test.txt
  ```

  **Commit**: YES (Wave 4 batch)

---

- [ ] T16. **/saas/signup endpoint — Stripe Checkout session create**

  **What to do**:
  - Create `BinStash.SaaS/Endpoints/SaasEndpoints.cs`
  - Map `POST /saas/signup` (unauthenticated — creates new account):
    - Accept body: `{ "email": "...", "tenantName": "..." }`
    - Validate inputs (non-empty, valid email format)
    - Create Stripe Checkout session: `SessionService.Create()` with mode=`subscription`, line items referencing the configured Stripe Price ID (`IConfiguration["Stripe:PriceId"]`), `success_url`, `cancel_url`, `metadata = { "tenantName": tenantName, "email": email }`
    - Return `{ "checkoutUrl": session.Url }` → client redirects to Stripe
    - Note: actual tenant provisioning happens in `checkout.session.completed` webhook (T14) — this endpoint only initiates checkout

  **Must NOT do**:
  - Do not provision the tenant here (webhook does it)
  - Do not expose Stripe API keys in the response
  - Do not map this route in OSS `Program.cs`

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Stripe Checkout API, input validation, async Stripe call
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 4 (parallel with T13, T14, T17)
  - **Parallel Group**: Wave 4
  - **Blocks**: F1-F4
  - **Blocked By**: T12

  **References**:
  - `BinStash.SaaS/Endpoints/` (T12 output — project scaffold)
  - Stripe Checkout API docs: https://docs.stripe.com/api/checkout/sessions/create
  - `IConfiguration["Stripe:PriceId"]` — usage-based price configured in Stripe dashboard

  **Acceptance Criteria**:
  - [ ] POST `/saas/signup` with valid body → HTTP 200 with `checkoutUrl`
  - [ ] POST with missing email → HTTP 400

  ```
  Scenario: Valid signup returns Checkout URL
    Tool: Bash (curl)
    Steps:
      1. curl -X POST http://localhost:5000/saas/signup -H "Content-Type: application/json" -d '{"email":"test@example.com","tenantName":"TestOrg"}'
    Expected Result: HTTP 200, response body contains "checkoutUrl" key with https://checkout.stripe.com URL
    Evidence: .sisyphus/evidence/task-16-signup.txt

  Scenario: Invalid signup returns 400
    Tool: Bash (curl)
    Steps:
      1. curl -X POST http://localhost:5000/saas/signup -H "Content-Type: application/json" -d '{"email":"","tenantName":""}'
    Expected Result: HTTP 400
    Evidence: .sisyphus/evidence/task-16-signup-invalid.txt
  ```

  **Commit**: YES (Wave 4 batch)

---

- [ ] T17. **/saas/portal endpoint — Stripe Customer Portal redirect**

  **What to do**:
  - In `BinStash.SaaS/Endpoints/SaasEndpoints.cs`, map `GET /saas/portal` (requires `TenantPermission.BillingAdmin` auth)
  - Look up `BillingProfile` for the authenticated tenant
  - Create Stripe Customer Portal session: `Stripe.BillingPortal.SessionService.Create()` with `CustomerId = billingProfile.StripeCustomerId`, `ReturnUrl = IConfiguration["App:BaseUrl"] + "/dashboard"`
  - Return HTTP 302 redirect to `session.Url`
  - If no `BillingProfile` found: return HTTP 404 ("No billing account found for this tenant")

  **Must NOT do**:
  - No custom billing UI — redirect only
  - Do not expose StripeCustomerId in the response body

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Simple redirect endpoint, single Stripe API call
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES — Wave 4
  - **Parallel Group**: Wave 4 (with T13, T14, T16)
  - **Blocks**: F1-F4
  - **Blocked By**: T12

  **References**:
  - `BinStash.SaaS/Endpoints/SaasEndpoints.cs` (T16 output — add to same file)
  - Stripe Customer Portal API: https://docs.stripe.com/api/customer_portal/sessions/create
  - `TenantPermission.BillingAdmin` — auth policy name in `BinStash.Server/`

  **Acceptance Criteria**:
  - [ ] `GET /saas/portal` authenticated as BillingAdmin → HTTP 302 to Stripe portal URL
  - [ ] `GET /saas/portal` without BillingProfile → HTTP 404

  ```
  Scenario: Portal redirect for authenticated tenant
    Tool: Bash (curl)
    Steps:
      1. curl -i -H "Authorization: Bearer <valid-admin-token>" http://localhost:5000/saas/portal
    Expected Result: HTTP 302, Location header contains checkout.stripe.com or billing.stripe.com
    Evidence: .sisyphus/evidence/task-17-portal.txt
  ```

  **Commit**: YES — `feat(saas): signup and portal endpoints`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists. For each "Must NOT Have": search codebase for forbidden patterns. Check evidence files exist. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build BinStash.slnx` + `dotnet test BinStash.slnx`. Review all changed files for `as Any`/`#pragma warning`, empty catches, commented-out code, unused usings. Verify 341+ tests pass.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | VERDICT`

- [ ] F3. **Real E2E QA** — `unspecified-high`
  Start server without `BillingPluginPath` → assert healthy. Start with invalid path → assert crash. POST ingest session with NoOp → assert no 402. GET release download without auth → assert 401. Run `Select-String` for Stripe pattern in OSS. Save evidence to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read spec vs actual diff. Verify 1:1. Detect cross-task contamination. Flag unaccounted changes.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

- **Wave 1**: `feat(billing): add billing interfaces and no-op foundation` — Core/Billing/, Contracts/Billing/
- **Wave 2**: `feat(billing): wire no-op provider and plugin loader` — Server/Billing/, Server/HostedServices/
- **Wave 3**: `feat(billing): quota enforcement and egress metering call sites` — Server/Endpoints/, Server/Grpc/
- **Wave 4**: Private repo — `feat(saas): initial Stripe billing plugin` — BinStash.SaaS/

---

## Success Criteria

```bash
# Zero Stripe references in OSS
Select-String -Path "BinStash.Core","BinStash.Contracts","BinStash.Infrastructure","BinStash.Server","BinStash.Cli" -Pattern "Stripe" -Recurse
# Expected: zero matches

# Build clean
dotnet build BinStash.slnx --configuration Release
# Expected: Build succeeded, 0 Error(s)

# All tests pass
dotnet test BinStash.slnx --configuration Release
# Expected: all 341+ passed

# Server starts without plugin (no crash)
$proc = Start-Process dotnet -ArgumentList "run --project BinStash.Server --urls=http://localhost:5777" -PassThru -NoNewWindow
Start-Sleep 8; $proc.HasExited
# Expected: False (still running, clean startup)

# Hardcoded password gone
Select-String -Path "BinStash.Server/Endpoints/ReleaseEndpoints.cs" -Pattern "D9BvHVpGlpaa9C8w230kQ8w8PIKUoc3k"
# Expected: zero matches
```
