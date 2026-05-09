# Progress

## Current status

Alpha. Core ingestion pipeline, pack-file storage, GraphQL management API, gRPC ingest, REST endpoints, multi-tenancy, and auth are implemented. No automated deployment pipeline exists. CliFx 3.0.0 upgrade is partially complete.

**Completed 2026-04-25:** Release flow optimization and V6 rdef migration. Build: 0 errors. 337 tests pass.
- **V6 rdef format:** `§0x02` stores FileHash (same as V4). V5 (StorageKey-based) erased. V5 deserializer retained for upgrade path.
- **Pack store keyed by FileHash:** `ObjectStore.WriteFileDefinitionAsync` now uses `FileDefinitionRecord.Deserialize(blob).FileHash` as the pack index key. `IndexedPackFileHandler` for fileDefs uses `FileHash` extractor.
- **`FileDefinition.StorageKey` DB column dropped** (EF migration `DropFileDefinitionStorageKey`).
- **`FileDefinitionStorageKeyComputer` deleted.**
- **Stats section (§0x0A)** now written by the serializer (was previously missing).
- **Round trip reductions:** Removed redundant `GetRepositoryAsync` (call #3) and full release list scan (call #2) from `ReleaseAddOrchestrator`. Minimum round trips: 4 (was 6).
- **gRPC batch dedup:** `UploadChunks` now checks existence in batches of 64 instead of per-chunk.
- **FastCDC block reads:** `ChunkUsingBuffer` uses 64KB block reads instead of `ReadByte()`.
- **gRPC pipeline:** `BinStashGrpcClient.UploadChunksAsync` uses producer-consumer `Channel<T>` (depth 4).
- **Streaming .rdef upload:** `BinStashApiClient.CreateReleaseAsync` uses `Pipe` instead of `MemoryStream`.
- **`IngestSession.IntendedRelease`** nullable string field added; stored from `CreateIngestSessionRequest` body.
- EF migration `AddIngestSessionIntendedRelease` added.

**Completed 2026-04-24:** Server codebase cleanup pass. Build: 0 errors.

**Completed 2026-04-15:** `BinStash.ChunkStoreExplorer` redesigned as a file-explorer-style split-pane TUI. Builds with 0 errors, 0 warnings.

**Completed 2026-04-14:** `BinStash.RepackFileDefs`, `BinStash.StoreMigration`, BINST-100, BINST-99, AOT fix, V1/V2 Length fix, frontend upgrade pipeline integration, BINST-93.

## What works

- Full ingestion pipeline (CLI → chunking → dedup → gRPC upload → REST finalize)
- V6 `.rdef` format (read V1–V6); V5 retained for upgrade path
- Pack-file storage with three-tier LSM-tree index (fileDefs keyed by FileHash)
- GraphQL management API (HotChocolate 15), REST endpoints, gRPC streaming ingest
- "Smart" composite auth (JWT Bearer / ApiKey / Cookie)
- Multi-tenancy (Single/Multi mode), RBAC
- Background services: SetupBootstrapper, ChunkStoreProbeService, ChunkStoreStatsHostedService, ReleaseUpgradeBackgroundService, ChunkStoreRebuildBackgroundService
- 24 CLI commands; SVN import subsystem (SQLite-backed resumable state)
- 32 EF Core migrations (auto-applied at startup)

## Test suite

| Project | Count |
|---|---|
| `BinStash.Core.Tests` | 283 |
| `BinStash.Serializers.Tests` | 44 |
| `BinStash.Server.Tests` | 10 |
| **Total** | **337** |

## Known gaps

- CliFx 3.0 migration incomplete
- S3 storage backend not implemented
- `chunk-store delete` / `release delete` throw `NotImplementedException`
- Stale docs (V2/V3 format, .NET 9 references)
- No integration/E2E tests
- No automated deployment pipeline


- Deleted empty directories/files: `BinStash.Core/ChunkStreaming/`, `ZipFormatDetector.cs`, `SingleTenantBootstrapper.cs`. Removed stale `.csproj` `<Compile Remove>` entry.
- Removed commented-out dead code: `GetReleaseStreamAsync`, `ChunkStoreShowCommand` duplicate, `ChunkStoreTestCommand`, `SingleTenantBootstrapper` registration.
- REST/GraphQL boundary: removed duplicate or GraphQL-covered REST endpoints from `TenantEndpoints`, `RepositoryEndpoints`, `ReleaseEndpoints`, `ServiceAccountEndpoints`. Fixed `Name = chunkStore.Name` → `Name = repo.Name` bug in `RepositoryEndpoints`.
- Removed duplicate `GetReleasesForRepository` from `TenantQueryService`.
- `SetupGateMiddleware` now returns `application/problem+json` with `setup_required` error code.
- `ChunkStoreStatsHostedService`: added `ILogger` injection, replaced silent `catch {}` with proper error logging.
- Fixed build errors introduced by cleanup: restored `using Microsoft.EntityFrameworkCore` in `ServiceAccountEndpoints`, corrected accidental `ListTenantsForMember` → `InviteMemberAsync` rename in `TenantEndpoints`.

**Completed 2026-04-15:** `BinStash.ChunkStoreExplorer` redesigned as a file-explorer-style split-pane TUI. Builds with 0 errors, 0 warnings.
- Replaced sequential CLI menu with keyboard-driven split-pane: left panel = navigable tree (Store → Category → PrefixGroup1 → PrefixGroup2 → Bucket → Files), right panel = node stats/detail.
- New write operations: **Rebuild segment from packs** (scan packs → decompress → BLAKE3 hash → sort+dedup → write `seg-000.idx`), **Rebuild bloom filter** (single segment or all segments in a bucket).
- All original features preserved (Store Overview, Bucket Browser, Pack/Segment/Bloom inspectors, FileDef decoder, Hash Lookup, Integrity Check, Verify Pack Offsets, Log Dump/Search, Raw Blob Read).
- Fix: moved `record`/`class` type declarations to end of file to resolve `CS8803` top-level statement ordering error.

**Completed 2026-04-14:** `BinStash.RepackFileDefs` cross-bucket repack tool. Fixes the bucket-mismatch bug in `BinStash.StoreMigration` that caused all 10 `StoreVerify` tests to fail with `KeyNotFoundException`.
- New project at `Utils/RepackFileDefs/` (console, net10.0, references Infrastructure + Blake3 2.2.1).
- `Program.cs` — 4-step pipeline: scan all FileDef packs → route each entry to correct storageKey prefix → write new correctly-routed pack files → delete stale index → call `ObjectStore.RebuildStorageAsync()`.
- `InternalsVisibleTo("BinStash.RepackFileDefs")` added to `BinStash.Infrastructure.csproj`.
- Added to `/Tooling/` folder in `BinStash.slnx`.
- Results on `C:\Tmp\BinStash\SecondLocalStoreSetup`: 194,857 entries repacked, rebuild OK, **all 10 StoreVerify tests PASS**.
- Build: 0 errors, 0 warnings.

**Completed 2026-04-14:** `BinStash.StoreMigration` standalone console tool. Repairs the on-disk FileDef store and PostgreSQL `FileDefinition.StorageKey` column after the BINST-99 LSM-tree + self-keying format change.
- New project at `BinStash.StoreMigration/` (console, net10.0, references Infrastructure + Npgsql 10.0.2).
- `OldFlatIndexReader.cs` — reads legacy `index{prefix}.idx` flat varint files (hash + fileNo + offset + length).
- `Program.cs` — 5-step pipeline: pg_dump backup → parallel prefix scan → write new IDX2 segments → delete stale files → update StorageKey in DB.
- `InternalsVisibleTo("BinStash.StoreMigration")` added to `BinStash.Infrastructure.csproj` for access to `PackFileEntry`, `SortedIndexSegment`, `IndexEntry`, `FileAtomicHelper`.
- Added to `/Tooling/` folder in `BinStash.slnx`.
- Build: 0 errors, 0 warnings. All 341 tests pass.

**Completed 2026-04-14:** BINST-100 (chunk store rebuild as async background job). Build is 0 errors. All 341 tests pass.


- **New files:** `FileAtomicHelper.cs`, `PackIndexBloomFilter.cs`, `SortedIndexSegment.cs` in `BinStash.Infrastructure/Storage/Indexing/`
- **Rewritten:** `IndexedPackFileHandler.cs` — three-tier lookup (log dict → bloom+binary-search on segments), append log with `LogFlushThreshold=4096`, log flush to sorted segment, size-tiered compaction (16:1, levels 0→1→2). All public signatures unchanged.
- **Updated:** `ObjectStore.cs` — lightweight stats path reads only 8-byte segment headers + log hash replay; no handler open required.
- **Build:** 0 errors, 0 warnings. All 341 tests pass.

**Completed 2026-04-14:** AOT fix for `BinStash.Cli` — `CredentialStore` now uses Windows DPAPI (`ProtectedData`) on Windows and AES-256-GCM on Linux/macOS instead of `Microsoft.AspNetCore.DataProtection`. This fixes the `Value cannot be null (Parameter 'dictionary')` runtime crash in AOT-published binaries. `Microsoft.AspNetCore.DataProtection` and `.Extensions` packages removed. `System.Security.Cryptography.ProtectedData` 9.0.5 added. AOT publish produces 0 IL warnings, 0 errors. `auth list` verified working on the AOT binary.

**Completed 2026-04-14:** BINST-91 (polymorphic ChunkStore backend settings) and BINST-92 (ChunkerOptions improvements). Both tickets moved to Done. Build succeeds (0 errors), all 341 tests pass. Runtime deserialization bug fixed — `PropertyNameCaseInsensitive = true` added to `JsonSerializerOptions` in `ChunkStoreEntityTypeConfiguration` to handle existing PascalCase JSON data in the database.

**Completed 2026-04-14:** BINST-93 epic (release upgrade pipeline rewrite). All backend sub-tasks complete:
- **BINST-94 (entity + migration):** Complete. `BackgroundJob` polymorphic entity created, old `ReleaseUpgradeJob` entity deleted, migration `ReplaceReleaseUpgradeJobsWithBackgroundJobs` generated.
- **BINST-95 (upgrade service):** Complete. `ReleaseUpgradeService` rewritten to use `ITopicEventSender` and `BackgroundJob` entity. BUG-04/ERR-03/PERF-05 fixes preserved.
- **BINST-96 (background service):** Complete. `ReleaseUpgradeBackgroundService` rewritten to use `BackgroundJob` entity.
- **BINST-97 (GraphQL subscriptions):** Complete. `Subscription.cs` + `SubscriptionType.cs` created. `Program.cs` wired with `.AddSubscriptionType<SubscriptionType>()`, `.AddInMemorySubscriptions()`, `app.UseWebSockets()`.
- **BINST-98 (REST endpoints):** Complete. `ChunkStoreEndpoints.cs` and `UpgradeJobEndpoints.cs` updated to use `BackgroundJob` entity with JSON deserialization.
- **Build:** Server builds with 0 errors. All 331 tests pass (283 Core + 48 Serializers).

**Completed 2026-04-14:** Frontend integration for release upgrade pipeline. The Vue 3 frontend has been connected to the backend's async upgrade pipeline:
- `graphql-ws` installed, Apollo Client rewritten with split link (HTTP + WebSocket)
- Vite proxy updated with `ws: true` for WebSocket subscriptions
- `upgradeChunkStore()` fixed (GET → POST), `UpgradeJobDto` and `ChunkStoreBackendSettingsDto` types added
- `src/api/upgradeJobs.ts` created (REST API functions for upgrade jobs)
- `src/composables/useBackgroundJobProgress.ts` created (GraphQL subscription composable)
- `src/pages/ChunkStoreDetail.vue` created (full detail page with upgrade UI, real-time progress, tabs)
- `/chunk-stores/:id` route added to router
- `ChunkStoreCard.vue` footer cleaned up ("Send Message" → "View Details")

**Completed 2026-04-14:** Bug fix — `OpaqueBlobBacking.Length must be set before serialization`. V1/V2 release formats did not store file lengths; the V4 serializer required them. Fixed by adding `PopulateMissingLengthsAsync()` in `ReleaseUpgradeService.ExecuteAsync()` which looks up null `Length` fields from the `FileDefinition` table (matching by `ContentHash`/`Checksum` + `ChunkStoreId`) before re-serialization. Handles both `OpaqueBlobBacking.Length` and `ContainerMemberBinding.Length`. Build: 0 errors, all 341 tests pass.

## What works (verified from code)

- **Ingestion pipeline** — `FastCdcChunker` (FastCDC, BLAKE3), plain-file and ZIP input formats, gRPC `UploadChunks` and `UploadFileDefinitions` streams, REST release registration.
- **Pack-file storage** — `LocalFolderChunkStoreStorage` / `ObjectStore` / `ObjectStoreManager` with `.pack` + segment index files; 4 GiB rotation. `IndexedPackFileHandler` with three-tier LSM-tree index (log dict → bloom+binary-search on MMF segments), `AsyncLruHandlerCache` (capacity 256). Lightweight stats path reads only segment headers (8 bytes each) + log hash replay without opening a handler.
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
- **Database** — 31 EF Core migrations, most recent: `2026-04-14 PolymorphicChunkStoreBackendSettings`. Auto-applied at startup.
- **ChunkStore backend settings** — Polymorphic `BackendSettings` column (`jsonb`) replaces hardcoded `LocalPath`. Uses `[JsonPolymorphic]` with `$type` discriminator. `LocalFolderBackendSettings` is the only concrete type. Pattern is extensible for S3/Azure backends.
- **ChunkerOptions** — Enhanced with comprehensive XML docs, `Validate()` method, clear separation of generic vs FastCDC-specific properties.

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
| **`SingleTenantBootstrapper` removed** | `SingleTenantBootstrapper.cs` deleted and its `Program.cs` registration removed. `SetupBootstrapper` handles single-tenant init. |
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
- Frontend / management UI (partially built; chunk store detail page with upgrade UI exists in Vue 3 frontend at `C:\Users\l.essmann\RiderProjects\Cruip\mosaic-vue`; other management areas still REST/GraphQL only).

## Evolution of key decisions

- **Deduplication is per-chunk-store** — intentional; no cross-store dedup planned at this time.
- **Auto-migration at startup** — `db.Database.Migrate()` in `Program.cs`; no manual `dotnet ef database update` in production.
- **`DbConfigurationSource` at index 0** — ensures database-stored settings are always overridable by env/appsettings.
- **`ingest.proto` field numbers must remain backward-compatible** — proto is a shared contract between server and CLI.
- **V4 `.rdef` format (2026-04-13)** — replaces V3 as the write format. Token-based string table (path segments instead of full paths). BackingIndex eliminated from §0x05 (implicit positional indexing). Artifacts sorted by path before serialisation (ordinal sort) — saves ~17 KB on the 11,049-artifact sample by giving Zstd prefix-run locality in §0x05 and §0x06. V4 is **30.4% smaller than V2** on the real sample. V1/V2/V3 deserialization preserved. `ComponentName` not stored on wire in V4 — derived from first path segment during deserialization. Artifact order after deserialization reflects on-disk sort order (path-alphabetical), not original insertion order.
- **Format experiments closed (2026-04-13)** — 9 experiments evaluated: cross-section concat, naive §0x05+§0x06 merge, delta-hash-index, outer Zstd passthrough for §0x02 — all rejected by measurement. Sort-by-path is the confirmed winner.
- **§0x02 hash compression deep dive (2026-04-14)** — EXP 2/EXP 9 discrepancy resolved: outer streaming Zstd on §0x02 is NOT wasteful; it compresses the already-Zstd'd transpose payload because `CompressionStream` (streaming) achieves ~107 KB while block `Compressor.Wrap()` cannot beat ~153 KB. 10 sub-experiments (10a–10k) run on the 3,447 unique sorted BLAKE3 hashes. Key findings:
  - Per-column entropy is ~7.94 bits (near-maximum for 256 unique byte values) — columns are essentially incompressible individually (Zstd ratio 100.3%, 3,457 B per 3,447 B column). Column 7 is the sole outlier at 12.0% ratio (414 B) — possibly related to hash distribution structure.
  - Higher inner/outer Zstd levels (L12–L19) yield **zero improvement** on unique hashes — the current L9 is already optimal.
  - **Arithmetic delta per column** saves 1,084 B (−1.0%) at L19, 1,032 B at L9. XOR delta saves only ~424 B. Marginal.
  - **Wider column groups hurt** on unique hashes (+2,674 to +2,851 B for widths 2–32). Current width-1 (32 separate columns) is optimal.
  - **Single Zstd stream** (all 32 columns concatenated, one frame) saves only 158–300 B. Negligible.
  - **No transpose at all** (raw sorted hashes) costs +2,671 B. Transpose is essential.
  - **XOR delta + wider column groups** still worse than baseline.
  - **Hash sort order is critical**: unsorted = 320 KB, sorted by hash = 107 KB, sorted by path = 189 KB.
  - **Conclusion**: V4 §0x02 is near-optimal for BLAKE3 hashes. The only viable gain is arithmetic delta encoding per column (+inner L19) for ~1 KB saving on 107 KB — a 1% improvement that adds format complexity. The hash data is fundamentally high-entropy after dedup+sort and cannot be compressed much further.
