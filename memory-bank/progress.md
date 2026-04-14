# Progress

## Current status

Alpha. Core ingestion pipeline, pack-file storage, GraphQL management API, gRPC ingest, REST endpoints, multi-tenancy, and auth are implemented. No automated deployment pipeline exists. CliFx 3.0.0 upgrade is partially complete (API migration has outstanding LSP errors).

## What works (verified from code)

- **Ingestion pipeline** — `FastCdcChunker` (FastCDC, BLAKE3), plain-file and ZIP input formats, gRPC `UploadChunks` and `UploadFileDefinitions` streams, REST release registration.
- **Pack-file storage** — `LocalFolderChunkStoreStorage` / `ObjectStore` / `ObjectStoreManager` with `.pack` + `.idx` files; 4 GiB rotation. `IndexedPackFileHandler` with memory-mapped index, lock-free reads, `AsyncLruHandlerCache` (capacity 256).
- **Release format** — `ReleasePackageSerializer` producing `.rdef` binary files; V4 format as of 2026-04-13 with sort-by-path optimization. V4 is 30.4% smaller than V2 on the real 11,049-artifact sample (161,049 B vs 231,289 B). V1/V2/V3 deserialization retained for backward compatibility.
- **GraphQL API** — HotChocolate 15 with `QueryType`, `MutationType`, filtering, sorting, projections, authorization.
- **REST endpoints** — `ChunkStoreEndpoints`, `IdentityEndpoints`, `IngestSessionEndpoints`, `InstanceEndpoints`, `ReleaseEndpoints`, `RepositoryEndpoints`, `ServiceAccountEndpoints`, `SetupEndpoints`, `StorageClassEndpoints`, `TenantEndpoints`.
- **Auth** — Composite "Smart" scheme (JWT Bearer / ApiKey / Cookie), "Setup" cookie scheme, ASP.NET Core Identity with confirmed-email requirement. ApiKey auth tested in `BinStash.Server.Tests`.
- **Multi-tenancy** — `TenancyMode.Single` / `TenancyMode.Multi`, `TenantResolutionMiddleware`, `SetupGateMiddleware`, `SetupBootstrapper`.
- **Background services** — `ChunkStoreProbeService`, `ChunkStoreStatsHostedService`, `SetupBootstrapper` (all registered and running).
- **CLI** — CliFx 3 + Spectre.Console; 24 commands (22 active, 2 stub): `auth` (login/logout/status/token), `chunk-store` (list/get/add), `repo` (list/get/add/delete), `release` (list/get/add/install/download), `analyze` (release/rdef), `svn-import-tags`, `test server`. REST and gRPC client wrappers in `BinStash.Cli/Infrastructure/`.
- **SVN import subsystem** — 8 files in `BinStash.Cli/Infrastructure/Svn/`, fully implemented with SQLite-backed resumable state, concurrent file reads, inline streaming chunking.
- **DB-backed configuration** — `DbConfigurationSource` at lowest priority.
- **Email** — Brevo provider via `BrevoEmailProvider`; Handlebars.Net `.hbs` templates embedded in `BinStash.Infrastructure`.
- **Health checks** — PostgreSQL + `ChunkStoreHealthCheck` at `/health` (requires `Permission:Instance:Admin`).
- **Database** — 30 EF Core migrations, most recent: `2026-03-22 BetterPerReleaseStatsTracking`. Auto-applied at startup.

## Test suite

| Test project | Focus | Test count |
|---|---|---|
| `BinStash.Core.Tests` | Unit + property-based: chunker, varint, Hash32/Hash8, BytesConverter, ZipMemberSelectionPolicy, DictionaryExtensions, BoundedStream, BitReader, ByteArrayComparer, StreamExtensions, ChecksumCompressor, ZipReconstructionPlanner, SubstringTableBuilder | ~283 |
| `BinStash.Serializers.Tests` | Round-trip tests for `.rdef` format (V2/V3/V4) | ~48 |
| `BinStash.Server.Tests` | ApiKeyAuthHandlerSpecs | 10 |
| **Total** | | ~341 |

- **Assertions:** FluentAssertions
- **Property-based:** FsCheck.Xunit
- **Snapshot/regression:** Verify.Xunit
- **Mutation testing:** Stryker config present (`BinStash.Core.Tests/stryker-config.json`)
- **Coverage:** `coverlet.collector`
- **Real sample `.rdef`:** Embedded as `EmbeddedResource` in `BinStash.Serializers.Tests.csproj` (11,049 artifacts, 119 components, 2,189 distinct path segments)
- **No integration or end-to-end tests yet.**

## Known gaps and issues

| Item | Detail |
|---|---|
| **CliFx 3.0 migration incomplete** | CLI project has LSP/compile errors from CliFx 3.0 API renames (namespaces, class names). Must be resolved before CLI builds. |
| **S3 chunk store not implemented** | Docs and CLI help reference S3 but no `IChunkStoreStorage` implementation exists. Only `LocalFolderChunkStoreStorage` is available. `ChunkStoreStorageFactory` throws `NotSupportedException` for non-Local types. |
| **`StorageStrategy.cs` excluded** | `BinStash.Core/Ingestion/Models/StorageStrategy.cs` excluded via `<Compile Remove=...>`. Role unclear. |
| **`SingleTenantBootstrapper` commented out** | In `Program.cs`. Single-tenant init relies solely on `SetupBootstrapper`. |
| **`chunk-store delete` / `release delete` stubs** | Present but throw `NotImplementedException`. |
| **Stale documentation** | `docs/faq.md` says ".NET 9". `docs/file-format.md` covers V2/V3 only (V4 has 10 sections). `docs/architecture.md` is a 3-line placeholder. `docs/cli-reference.md` missing auth/analyze/svn/test commands. |
| **No automated deployment** | Deployment is manual; no pipeline for container publishing or environment promotion. |
| **No integration tests** | Only unit and snapshot tests exist. |

## What's left to build (known backlog from code inspection)

- Complete CliFx 3.0 API migration (fix compile errors in CLI project).
- S3 chunk store storage backend implementation.
- Implement `chunk-store delete` and `release delete` commands.
- Expand test coverage (integration tests, end-to-end tests).
- Complete or remove `StorageStrategy.cs`.
- Update stale documentation (faq, file-format, architecture, cli-reference).
- Frontend / management UI (currently none; management is via GraphQL + REST only).

## Evolution of key decisions

- **Deduplication is per-chunk-store** — intentional; no cross-store dedup planned at this time.
- **Auto-migration at startup** — `db.Database.Migrate()` in `Program.cs`; no manual `dotnet ef database update` in production.
- **`DbConfigurationSource` at index 0** — ensures database-stored settings are always overridable by env/appsettings.
- **`ingest.proto` field numbers must remain backward-compatible** — proto is a shared contract between server and CLI.
- **V4 `.rdef` format (2026-04-13)** — replaces V3 as the write format. Token-based string table (path segments instead of full paths). BackingIndex eliminated from §0x05 (implicit positional indexing). Artifacts sorted by path before serialisation (ordinal sort) — saves ~17 KB on the 11,049-artifact sample by giving Zstd prefix-run locality in §0x05 and §0x06. V4 is **30.4% smaller than V2** on the real sample. V1/V2/V3 deserialization preserved. `ComponentName` not stored on wire in V4 — derived from first path segment during deserialization. Artifact order after deserialization reflects on-disk sort order (path-alphabetical), not original insertion order.
- **Format experiments closed (2026-04-13)** — 9 experiments evaluated: cross-section concat, naive §0x05+§0x06 merge, delta-hash-index, outer Zstd passthrough for §0x02 — all rejected by measurement. Sort-by-path is the confirmed winner.
