# Progress

## Current status

Alpha. Core ingestion pipeline, pack-file storage, GraphQL management API, gRPC ingest, REST endpoints, multi-tenancy, and auth are implemented and compile cleanly. No automated deployment pipeline exists.

## What works (verified from code)

- **Ingestion pipeline** — `FastCdcChunker` (FastCDC, BLAKE3), plain-file and ZIP input formats, gRPC `UploadChunks` and `UploadFileDefinitions` streams, REST release registration.
- **Pack-file storage** — `LocalFolderChunkStoreStorage` / `ObjectStore` / `ObjectStoreManager` with `.pack` + `.idx` files; 4 GiB rotation.
- **Release format** — `ReleasePackageSerializer` producing `.rdef` binary files; V4 format as of 2026-04-13. Format stability guarded by round-trip tests in `BinStash.Serializers.Tests`. V1/V2/V3 deserialization retained for backward compatibility.
- **GraphQL API** — HotChocolate 15 with `QueryType`, `MutationType`, filtering, sorting, projections, authorization.
- **REST endpoints** — `ChunkStoreEndpoints`, `IdentityEndpoints`, `IngestSessionEndpoints`, `InstanceEndpoints`, `ReleaseEndpoints`, `RepositoryEndpoints`, `ServiceAccountEndpoints`, `SetupEndpoints`, `StorageClassEndpoints`, `TenantEndpoints`.
- **Auth** — Composite "Smart" scheme (JWT Bearer / ApiKey / Cookie), "Setup" cookie scheme, ASP.NET Core Identity with confirmed-email requirement.
- **Multi-tenancy** — `TenancyMode.Single` / `TenancyMode.Multi`, `TenantResolutionMiddleware`, `SetupGateMiddleware`, `SetupBootstrapper`.
- **Background services** — `ChunkStoreProbeService`, `ChunkStoreStatsHostedService`, `SetupBootstrapper` (all registered and running).
- **CLI** — CliFx 2 + Spectre.Console; commands: `auth`, `chunk-store`, `repo`, `release`, `analyze`, `svn-import-tags`, `test`; REST and gRPC client wrappers in `BinStash.Cli/Infrastructure/`.
- **DB-backed configuration** — `DbConfigurationSource` at lowest priority.
- **Email** — Brevo provider via `BrevoEmailProvider`; Handlebars.Net `.hbs` templates embedded in `BinStash.Infrastructure`.
- **Health checks** — PostgreSQL + `ChunkStoreHealthCheck` at `/health` (requires `Permission:Instance:Admin`).
- **Tests** — `BinStash.Core.Tests` (unit: chunker, varint; property-based via FsCheck; snapshot via Verify); `BinStash.Serializers.Tests` (round-trip tests for `.rdef` format, including V4 tokenized-path tests). Stryker mutation testing config present.

## Known gaps and issues

| Item | Detail |
|---|---|
| **Dockerfile base image mismatch** | `BinStash.Server/Dockerfile` uses `aspnet:9.0` / `sdk:9.0`; all projects target `net10.0`. Must update to `aspnet:10.0` / `sdk:10.0`. |
| **README badge mismatch** | `README.md` shows `.NET 9.0` badge; projects target `net10.0`. |
| **S3 chunk store not implemented** | Server README and CLI docs reference S3 as a chunk store type, but no S3 `IChunkStoreStorage` implementation exists. Only `LocalFolderChunkStoreStorage` is available. |
| **`StorageStrategy.cs` excluded from compilation** | `BinStash.Core/Ingestion/Models/StorageStrategy.cs` is excluded via `<Compile Remove=...>`. Its role is unclear; do not reference it. |
| **`SingleTenantBootstrapper` commented out** | Registered but commented out in `Program.cs`. Single-tenant init relies solely on `SetupBootstrapper`. |
| **Test coverage expanded** | Tier 1–3 unit tests added. Tier 1: `Hash32`, `Hash8`, `BytesConverter`, `ZipMemberSelectionPolicy`, `DictionaryExtensions`, `BoundedStream`, `BitReader`, `ByteArrayComparer`, `StreamExtensions`. Tier 2: `ChecksumCompressor` (22 tests), `ZipReconstructionPlanner` (34 tests). Tier 3: `ReleasePackageSerializer` round-trip tests (39 tests in `BinStash.Serializers.Tests`) and `SubstringTableBuilder` tokenizer tests (21 tests in `BinStash.Core.Tests`). **Bug fixed**: `SubstringTableBuilder.Tokenize` — removed `if (i > start)` guard; consecutive separators (e.g. `://` in URLs) no longer silently dropped. V4 format: 8 additional round-trip tests (including mixed opaque+reconstructed interleave test). Total: 331 tests passing (283 Core + 48 Serializers). No integration or end-to-end tests yet. Real sample rdef: 11,049 artifacts, 119 components, 2,189 distinct path segments; **V4 = 161,049 B vs V2 = 231,289 B (-30.4%)**. `.rdef` file now embedded as `EmbeddedResource` in `BinStash.Serializers.Tests.csproj`. |
| **No automated deployment** | Deployment is manual; no pipeline for container publishing or environment promotion exists in this repository. |

## What's left to build (known backlog from code inspection)

- S3 chunk store storage backend implementation.
- Fix Dockerfile to use .NET 10 base images.
- Expand test coverage (integration tests, V2 deserialization round-trip if needed).
- Complete or remove `StorageStrategy.cs`.
- Finalize `SvnImportTagsCommand` (exists but may be incomplete).
- Frontend / management UI (currently none; management is via GraphQL + REST only).

## Evolution of key decisions

- **Deduplication is per-chunk-store** — intentional; no cross-store dedup planned at this time.
- **Auto-migration at startup** — `db.Database.Migrate()` in `Program.cs`; no manual `dotnet ef database update` in production.
- **`DbConfigurationSource` at index 0** — ensures database-stored settings are always overridable by env/appsettings.
- **`ingest.proto` field numbers must remain backward-compatible** — proto is a shared contract between server and CLI.
- **V4 `.rdef` format (2026-04-13)** — replaces V3 as the write format. Token-based string table (path segments instead of full paths). BackingIndex eliminated from §0x05 (implicit positional indexing). Artifacts sorted by path before serialisation (ordinal sort) — saves ~17 KB on the 11,049-artifact sample by giving Zstd prefix-run locality in §0x05 and §0x06. V4 is **30.4% smaller than V2** on the real sample (161,049 B vs 231,289 B). V1/V2/V3 deserialization preserved. `ComponentName` is no longer stored on the wire in V4; it is derived from the first `/`-separated segment of `Path` during deserialization. Artifact order after deserialization reflects on-disk sort order (path-alphabetical), not original insertion order — consumers must not assume positional stability.
