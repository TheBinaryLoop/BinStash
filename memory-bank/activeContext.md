# Active Context

## Current work focus

As of 2026-04-13, the Memory Bank has been fully updated to reflect a comprehensive repo scan. Prior work completed BINST-66 (Dockerfile/.NET 10 fix), V4 `.rdef` format implementation, and extensive test suite expansion.

## Recent changes (observed from code)

- Target framework is `net10.0` across all projects; Dockerfile uses `aspnet:10.0` / `sdk:10.0`.
- Solution uses `.slnx` (XML-based format), not classic `.sln`.
- CliFx upgraded to 3.0.0 (major version) — API migration partially complete (LSP errors remain in CLI project from namespace/class renames).
- V4 release format implemented with sort-by-path optimization (−30.4% vs V2).
- `BinStash.Core` has `Ingestion/Models/StorageStrategy.cs` excluded from compilation (role unclear).
- `SingleTenantBootstrapper` hosted service is registered but commented out in `Program.cs`.
- Scalar API reference UI is wired for Development mode (`/scalar`).
- `BinStash.Server.Tests` project added with `ApiKeyAuthHandlerSpecs.cs` (10 tests).
- Bug fix applied to `SubstringTableBuilder.Tokenize` — consecutive separators (e.g., `://` in URLs) no longer silently dropped.

## Known discrepancies / items requiring attention

- **CliFx 3.0.0 migration incomplete:** CLI project has LSP errors from CliFx 3.0 API changes (`CliFx.Binding` namespace, `CommandLineApplicationBuilder`, `UseTypeInstantiator`, `ScalarInputConverter`/`SequenceInputConverter` renames). Code may not compile until migration is finished.
- **`StorageStrategy.cs` excluded from compilation:** `BinStash.Core/Ingestion/Models/StorageStrategy.cs` excluded via `<Compile Remove=...>`. Do not reference it.
- **S3 chunk store not implemented:** Referenced in docs and CLI help text but no `IChunkStoreStorage` implementation exists. Only `LocalFolderChunkStoreStorage` is available.
- **`SingleTenantBootstrapper` commented out:** Registered but commented out in `Program.cs`. Single-tenant init relies solely on `SetupBootstrapper`.
- **Stale documentation:** `docs/faq.md` references ".NET 9"; `docs/file-format.md` documents only V2/V3 (V4 has 10 sections vs the 5 documented); `docs/architecture.md` is a 3-line placeholder; `docs/cli-reference.md` is missing auth/analyze/svn/test commands.
- **`chunk-store delete` and `release delete` commands:** Present but throw `NotImplementedException`.
- **`appsettings.Development.json`** uses `Email2` section (not `Email`), effectively disabling email in dev.

## Active decisions and preferences

- Deduplication scope is intentionally per-chunk-store, not global.
- Auto-migration at startup is the chosen migration strategy; no manual `dotnet ef database update` in production.
- JWT key fallback (`"dev-only-change-me"`) must never reach production — flagged in code comments.
- V4 `.rdef` format is the current write format; V1/V2/V3 deserialization retained for backward compatibility.
- Artifacts sorted by path before V4 serialization — consumers must not assume positional stability after deserialization.

## Important patterns observed

- All source files carry the AGPLv3 copyright header with author `Lukas Eßmann`.
- Authorization policies are consistently named `Permission:<Scope>:<Level>`.
- `DbConfigurationSource` is inserted at index 0 (lowest priority) so database-stored settings are always overridable.
- CLI uses constructor DI via `ServiceCollection` in `Program.cs` `UseTypeInstantiator` (CliFx 3.0 API); commands are transient, services are singletons.
- Ingestion pipeline in Core supports two input formats: plain directory (`PlainFileFormatHandler`) and ZIP archives (`ZipFormatHandler`).
- `ReleaseAddOrchestrator`, `ServerUploadPlanner`, and `ComponentMapLoader` are the CLI-side orchestration services for `release add`.
- `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC) in `BinStash.Cli/Infrastructure/` are the server communication wrappers.
- SVN import subsystem (`BinStash.Cli/Infrastructure/Svn/`, 8 files) is fully implemented with SQLite-backed resumable state, concurrent file reads, and inline streaming chunking.
