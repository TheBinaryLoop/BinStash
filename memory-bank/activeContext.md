# Active Context

## Current work focus

As of 2026-04-14, completed BINST-91 (polymorphic ChunkStore backend settings) and BINST-92 (ChunkerOptions improvements). Both tickets moved to Done.

## Recent changes (observed from code)

- **ChunkStore entity refactored (BINST-91):** `LocalPath` property replaced with polymorphic `BackendSettings` (`ChunkStoreBackendSettings` base class, `LocalFolderBackendSettings` concrete type). Uses `System.Text.Json` `[JsonPolymorphic]` with `$type` discriminator, stored as `jsonb` column in PostgreSQL.
- **ChunkerOptions improved (BINST-92):** Added comprehensive XML docs separating generic vs FastCDC-specific properties, `Validate()` method for per-ChunkerType validation.
- **EF Core migration generated:** `PolymorphicChunkStoreBackendSettings` migration with data migration SQL to convert existing `LocalPath` values into `BackendSettings` JSON.
- **GraphQL updated:** `ChunkStoreGql` now includes `BackendSettings` field via `ChunkStoreBackendSettingsGql` DTO. `ChunkStoreQueryService` loads entities in-memory and maps backend settings.
- **REST endpoints updated:** `ChunkStoreEndpoints` and `SetupEndpoints` use `BackendSettings` instead of `LocalPath`.
- **Storage factory and probe service updated:** `ChunkStoreStorageFactory` uses `GetBackendSettings<T>()`. `ChunkStoreProbeService` pattern-matches on `LocalFolderBackendSettings`.
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
- **Stale documentation:** `docs/faq.md` references ".NET 9"; `docs/file-format.md` documents only V2/V3 (V4 has 10 sections vs the 5 documented); `docs/architecture.md` is a 3-line placeholder; `docs/cli-reference.md` is missing auth/analyze/svn/test commands.
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

## Important patterns observed

- All source files carry the AGPLv3 copyright header with author `Lukas Eßmann`.
- Authorization policies are consistently named `Permission:<Scope>:<Level>`.
- `DbConfigurationSource` is inserted at index 0 (lowest priority) so database-stored settings are always overridable.
- CLI uses constructor DI via `ServiceCollection` in `Program.cs` `UseTypeInstantiator` (CliFx 3.0 API); commands are transient, services are singletons.
- Ingestion pipeline in Core supports two input formats: plain directory (`PlainFileFormatHandler`) and ZIP archives (`ZipFormatHandler`).
- `ReleaseAddOrchestrator`, `ServerUploadPlanner`, and `ComponentMapLoader` are the CLI-side orchestration services for `release add`.
- `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC) in `BinStash.Cli/Infrastructure/` are the server communication wrappers.
- SVN import subsystem (`BinStash.Cli/Infrastructure/Svn/`, 8 files) is fully implemented with SQLite-backed resumable state, concurrent file reads, and inline streaming chunking.
