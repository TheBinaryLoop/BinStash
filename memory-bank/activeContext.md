# Active Context

## Current work focus

As of 2026-04-14, the main effort is **rewriting the release upgrade pipeline** (BINST-93 epic) to be async, safe, and observable. This involves:
- Replacing the old synchronous `UpgradeReleasesToLatestVersionAsync` endpoint with an async background job pipeline.
- Fixing three critical bugs: BUG-04 (data loss), ERR-03 (inconsistency), PERF-05 (performance).
- Adding real-time progress via HotChocolate GraphQL subscriptions (no SignalR).
- Using a polymorphic `BackgroundJob` entity to support future job types.

Previously completed: BINST-91 (polymorphic ChunkStore backend settings) and BINST-92 (ChunkerOptions improvements).

## Recent changes (observed from code)

### Release upgrade pipeline rewrite (BINST-93, in progress)

- **`BackgroundJob` entity created:** Polymorphic job entity at `BinStash.Core/Entities/BackgroundJob.cs` with `JobType` string discriminator, `Status` enum (`Pending`, `Running`, `Completed`, `Failed`, `Cancelled`), and JSON payload columns (`JobData`, `ProgressData`, `ErrorDetails` as `jsonb`). Job-type-specific data classes: `ReleaseUpgradeJobData`, `ReleaseUpgradeProgressData`.
- **Old `ReleaseUpgradeJob` entity deleted:** Replaced by `BackgroundJob`. Old entity file, EF config, and migration removed.
- **EF Core migration generated:** `ReplaceReleaseUpgradeJobsWithBackgroundJobs` — drops `ReleaseUpgradeJobs` table, creates `BackgroundJobs` table with `JobType` and `Status` indexes.
- **`ReleaseUpgradeService` rewritten:** Now uses `ITopicEventSender` (HotChocolate) instead of SignalR `IHubContext`. Broadcasts `BackgroundJobProgressDto` to topic `BackgroundJobProgress_{jobId}`. Core upgrade algorithm preserves BUG-04/ERR-03/PERF-05 fixes (write-new-then-delete-old, batch SaveChanges).
- **`ReleaseUpgradeBackgroundService` rewritten:** Uses `Channel<Guid>` job queue. Startup recovery queries `db.BackgroundJobs` filtering by `BackgroundJobTypes.ReleaseUpgrade`.
- **`ChunkStoreEndpoints.cs` updated:** `StartUpgradeReleasesAsync` now creates `BackgroundJob` with serialized `ReleaseUpgradeJobData`. Duplicate detection queries `BackgroundJobs` with `JobType` filter. Description updated from SignalR to GraphQL subscriptions.
- **`UpgradeJobEndpoints.cs` updated:** All three endpoints (`Get`, `Cancel`, `List`) rewritten to query `db.BackgroundJobs` with `JobType == BackgroundJobTypes.ReleaseUpgrade` filter. `MapToDto` deserializes JSON payload columns. Made `internal` for cross-file reuse.
- **GraphQL subscriptions wired up:**
  - `Subscription.cs` root class with `BackgroundJobProgress(jobId)` subscription resolver using `[Subscribe]` + `ITopicEventReceiver`.
  - `SubscriptionType.cs` fluent descriptor following existing `QueryType`/`MutationType` pattern.
  - `Program.cs` updated: `.AddSubscriptionType<SubscriptionType>()`, `.AddInMemorySubscriptions()`, `app.UseWebSockets()` before `app.MapGraphQL()`.
- **Build status:** Server builds with 0 errors, all 331 tests pass (283 Core + 48 Serializers).

### Previous changes

- **ChunkStore entity refactored (BINST-91):** `LocalPath` property replaced with polymorphic `BackendSettings` (`ChunkStoreBackendSettings` base class, `LocalFolderBackendSettings` concrete type). Uses `System.Text.Json` `[JsonPolymorphic]` with `$type` discriminator, stored as `jsonb` column in PostgreSQL.
- **ChunkerOptions improved (BINST-92):** Added comprehensive XML docs separating generic vs FastCDC-specific properties, `Validate()` method for per-ChunkerType validation.
- Target framework is `net10.0` across all projects; Dockerfile uses `aspnet:10.0` / `sdk:10.0`.
- Solution uses `.slnx` (XML-based format), not classic `.sln`.
- CliFx upgraded to 3.0.0 (major version) — API migration partially complete (LSP errors remain in CLI project from namespace/class renames).
- V4 release format implemented with sort-by-path optimization (−30.4% vs V2).

## Known discrepancies / items requiring attention

- **BackendSettings JSON case sensitivity fixed (2026-04-14):** The `JsonSerializerOptions` in `ChunkStoreEntityTypeConfiguration` originally used `PropertyNamingPolicy = CamelCase` without `PropertyNameCaseInsensitive = true`. The migration wrote PascalCase property names (`"Path"`) into existing rows, but the deserializer expected camelCase (`"path"`), causing `NotSupportedException` at runtime. Fixed by adding `PropertyNameCaseInsensitive = true` — now reads both `"Path"` and `"path"` correctly; new writes continue in camelCase.
- **CliFx 3.0.0 migration incomplete:** CLI project has LSP errors from CliFx 3.0 API changes (`CliFx.Binding` namespace, `CommandLineApplicationBuilder`, `UseTypeInstantiator`, `ScalarInputConverter`/`SequenceInputConverter` renames). Code may not compile until migration is finished.
- **`StorageStrategy.cs` excluded from compilation:** `BinStash.Core/Ingestion/Models/StorageStrategy.cs` excluded via `<Compile Remove=...>`. Do not reference it.
- **S3 chunk store not implemented:** Referenced in docs and CLI help text but no `IChunkStoreStorage` implementation exists. Only `LocalFolderChunkStoreStorage` is available. The polymorphic `BackendSettings` pattern is now ready to support future S3/Azure backends.
- **`SingleTenantBootstrapper` commented out:** Registered but commented out in `Program.cs`. Single-tenant init relies solely on `SetupBootstrapper`.
- **Stale documentation:** `docs/faq.md` references ".NET 9"; `docs/file-format.md` documents only V2/V3 (V4 has 10 sections); `docs/architecture.md` is a 3-line placeholder; `docs/cli-reference.md` is missing auth/analyze/svn/test commands.
- **`chunk-store delete` and `release delete` commands:** Present but throw `NotImplementedException`.
- **`appsettings.Development.json`** uses `Email2` section (not `Email`), effectively disabling email in dev.
- **CLI `chunk-store show` command:** Should be updated to display `BackendSettings` instead of `LocalPath`, but CLI has pre-existing CliFx compile errors.

## Active decisions and preferences

- Deduplication scope is intentionally per-chunk-store, not global.
- Auto-migration at startup is the chosen migration strategy; no manual `dotnet ef database update` in production.
- JWT key fallback (`"dev-only-change-me"`) must never reach production — flagged in code comments.
- V4 `.rdef` format is the current write format; V1/V2/V3 deserialization retained for backward compatibility.
- Artifacts sorted by path before V4 serialization — consumers must not assume positional stability after deserialization.
- **ChunkStore backend settings use JSON polymorphism** (`[JsonPolymorphic]` with `$type` discriminator in `jsonb` column) — extensible for future storage backend types.
- **Background jobs use polymorphic `BackgroundJob` entity** with JSON payload columns — designed to support multiple job types without per-type tables.
- **No SignalR** — real-time updates use HotChocolate GraphQL subscriptions over WebSockets.

## Important patterns observed

- All source files carry the AGPLv3 copyright header with author `Lukas Eßmann`.
- Authorization policies are consistently named `Permission:<Scope>:<Level>`.
- `DbConfigurationSource` is inserted at index 0 (lowest priority) so database-stored settings are always overridable.
- CLI uses constructor DI via `ServiceCollection` in `Program.cs` `UseTypeInstantiator` (CliFx 3.0 API); commands are transient, services are singletons.
- Ingestion pipeline in Core supports two input formats: plain directory (`PlainFileFormatHandler`) and ZIP archives (`ZipFormatHandler`).
- `ReleaseAddOrchestrator`, `ServerUploadPlanner`, and `ComponentMapLoader` are the CLI-side orchestration services for `release add`.
- `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC) in `BinStash.Cli/Infrastructure/` are the server communication wrappers.
- SVN import subsystem (`BinStash.Cli/Infrastructure/Svn/`, 8 files) is fully implemented with SQLite-backed resumable state, concurrent file reads, and inline streaming chunking.
