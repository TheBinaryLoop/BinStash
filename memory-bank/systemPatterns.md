# System Patterns

## Architecture style

Modular monolith with a layered project structure. Single deployable server binary, separate CLI executable. No microservices.

## Project dependency graph

```
BinStash.Contracts   (no internal deps)
       ↑
BinStash.Core        (depends on Contracts)
       ↑
BinStash.Infrastructure  (depends on Core)
       ↑
BinStash.Server      (depends on Core + Infrastructure + Contracts)
BinStash.Cli         (depends on Core + Contracts only)

Test projects:
BinStash.Core.Tests        → Core
BinStash.Serializers.Tests → Core + Contracts
BinStash.Server.Tests      → Server (uses EF Core InMemory)
```

**Rule:** Core must never reference Infrastructure or Server. Contracts must never reference any other BinStash project.

## Ingestion pipeline

1. CLI discovers files on disk via `IInputDiscoveryService`. Input may be a plain directory or a ZIP archive (`IInputFormatHandler` implementations: `PlainFileFormatHandler`, `ZipFormatHandler`).
2. Files are chunked by `FastCdcChunker` using the FastCDC algorithm; each chunk is hashed with BLAKE3.
3. CLI negotiates with server (gRPC `UploadChunks` stream) — server returns dedup stats.
4. Only missing chunks are transmitted.
5. File definitions are serialized to the `.rdef` format and streamed via gRPC `UploadFileDefinitions`.
6. CLI calls REST endpoint to register the release in the database.

CLI-side orchestration: `ReleaseAddOrchestrator` → `ServerUploadPlanner` + `ComponentMapLoader`; server communication via `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC).

## Pack-file storage

- Chunks and file definitions are stored in `.pack` files, grouped by BLAKE3 hash prefix.
- Each pack entry: magic header `BSPK` (4 bytes), version byte (1), uncompressed length (4), compressed length (4), xxHash3 checksum (8) — 21-byte header total, followed by data.
- An `.idx` sidecar file stores absolute byte offsets + lengths for O(1) random access.
- Pack files rotate at 4 GiB.
- `ObjectStore` / `ObjectStoreManager` manage pack file lifecycle.
- `LocalFolderChunkStoreStorage` is the only implemented `IChunkStoreStorage` backend.
- `ChunkStoreStorageFactory` only supports `ChunkStoreType.Local`; throws `NotSupportedException` for other types.

### Storage implementation internals

- **`IndexedPackFileHandler`** — internal class that handles individual pack files. Uses memory-mapped index loading, lock-free reads via `ConcurrentDictionary<Hash32, IndexEntry>`, and supports LRU eviction.
- **`AsyncLruHandlerCache<TKey>`** — generic async LRU cache (capacity 256) for pack file handler instances. Ensures a bounded number of pack files are open simultaneously.
- **Pack entry format (`PackFileEntry`):** `BSPK` magic (4B) + version (1B) + uncompressed length (4B) + compressed length (4B) + xxHash3 checksum (8B) = 21-byte header.

## Release format (.rdef)

- Custom binary format produced by `ReleasePackageSerializer`.
- **Current version: V4** (write path as of 2026-04-13). V1/V2/V3 deserialization retained for backward compatibility.
- Fixed 6-byte header: magic `BPKG` (4 bytes) + version byte + flags byte.
- Sections (each prefixed by id byte + flags byte + varint length): all individually Zstd-compressed when `EnableCompression = true`.
- **V4 section layout:**
  - `0x01` metadata: version string, releaseId, repoId, notes, createdAt (varint Unix seconds)
  - `0x02` content hashes: BLAKE3 hashes via `ChecksumCompressor.TransposeCompress` (column-transposition + per-column Zstd)
  - `0x03` token table: deduplicated, byte-sorted path *segments* (not full paths). Each entry: varint length + UTF-8 bytes. All lengths first, then all bytes.
  - `0x04` custom properties: varint count + (key_token_idx, value_token_idx) pairs
  - `0x05` output artifacts: varint count + per-artifact record: `[seg_count] [seg0_idx] ... [segN_idx] [kind byte] [bytePerfect byte] [backingType byte]` — no `ComponentNameIndex`, no `BackingIndex`; backing index is implicit (k-th artifact of BackingType=OpaqueBlob maps to k-th entry in §0x06; k-th ReconstructedContainer maps to k-th entry in §0x07)
  - `0x06` opaque backings: varint count + (contentHashIndex varint, length varint) pairs
  - `0x07` reconstructed backings: varint count + (formatIdTokenIdx, reconstructionKind byte, memberStart varint, memberCount varint, recipePayloadIndex varint)
  - `0x08` container members: varint count + per-member: `[seg_count] [seg0_idx] ... [segN_idx] [contentHashIndex varint] [length varint]`
  - `0x09` recipe payloads: varint count + (varint length + bytes) per payload
  - `0x0A` stats: componentCount, fileCount, chunkCount, rawSize, dedupedSize (all varints)
- **V4 size analysis (real 11,049-artifact dataset):**
  - V4 compressed: 161,049 B vs V2: 231,289 B (−30.4%)
  - Artifacts sorted by path (ordinal) before serialization — adjacent artifacts share path-token prefix runs, giving Zstd better locality
  - §0x02 (content-hashes): dominant section, unavoidable (pure data + internal transpose+Zstd)
- **`ComponentName` in V4:** Not stored on wire. On deserialization, derived as `tokenTable[pathTokenIndices[0]]` (first path segment). The `OutputArtifact.ComponentName` property on the domain model is still populated.
- Varint encoding for sizes via `VarIntUtils` (LEB128).
- Round-trip tests in `BinStash.Serializers.Tests` guard correctness.

## Authentication and authorization

- Composite "Smart" scheme: selects JWT Bearer, ApiKey, or Identity cookie based on `Authorization` header prefix.
- Separate "Setup" cookie scheme for first-run bootstrapping.
- Three authorization scopes: Instance, Tenant, Repository.
- Permission policies: `Permission:Instance:Admin`, `Permission:Tenant:*`, `Permission:Repo:*`.
- `ApiKey` entities are stored in the database with hashed secrets.
- `ApiKeyAuthHandler` tested in `BinStash.Server.Tests` (10 tests).

## Multi-tenancy

- Mode controlled by `TenancySettings.Mode` (`Single` | `Multi`).
- `TenantResolutionMiddleware` resolves the active tenant on each request.
- `TenantContext` (scoped) carries the resolved tenant through the request pipeline.
- `SetupGateMiddleware` gates access until first-run setup is complete.
- `SetupBootstrapper` (hosted service) runs on startup to initialize setup state.

## GraphQL API

- HotChocolate 15 with filtering, sorting, and projections enabled.
- Query type: `QueryType`; Mutation type: `MutationType`.
- GraphQL services live in `BinStash.Server/GraphQL/Services/`.
- Cost limit enforcement is disabled (`EnforceCostLimits = false`).

## Background services

| Service | Purpose |
|---|---|
| `SetupBootstrapper` | First-run initialization |
| `ChunkStoreProbeService` | Liveness checks for chunk stores; results cached in `ChunkStoreProbeCache` |
| `ChunkStoreStatsHostedService` | Periodic stats snapshot collection via `ChunkStoreStatsCollector` |

## Configuration layering

Priority (highest to lowest):
1. Environment variables
2. `appsettings.{Environment}.json`
3. `appsettings.json`
4. User secrets (dev only)
5. `DbConfigurationSource` — instance settings from the database (inserted at index 0)

## Key data entities (BinStashDbContext)

`ApiKey`, `BinStashUser`, `Chunk`, `ChunkStore`, `ChunkStoreStatsSnapshot`, `FileDefinition`, `IngestSession`, `Release`, `ReleaseMetrics`, `Repository`, `RepositoryRoleAssignment`, `ServiceAccount`, `StorageClass`, `StorageClassMapping`, `StorageClassDefaultMapping`, `Subscription`, `Tenant`, `TenantMember`, `TenantMemberInvitation`, `TenantRoleAssignment`, `UserGroup`, `UserGroupMember`, `UserRefreshToken`, `InstanceSetting`, `SetupState`, `SetupCode`

## gRPC contract

Proto: `BinStash.Contracts/Protos/ingest.proto`  
Service: `IngestService`  
RPCs:
- `UploadChunks(stream UploadChunkRequest) → UploadChunksReply`
- `UploadFileDefinitions(stream UploadFileDefinitionsRequest) → UploadFileDefinitionsReply`

Both server and CLI reference this proto file directly. Field numbers must remain backward-compatible.

## CLI architecture

- **Framework:** CliFx 3.0.0 with `CommandLineApplicationBuilder` entry point.
- **DI:** `UseTypeInstantiator` with `ServiceCollection`; commands are transient, services are singletons.
- **24 commands** (22 active, 2 commented out: `chunk-store delete`, `release delete` — both throw `NotImplementedException`).
- **Command groups:** `auth` (login/logout/status/token), `chunk-store` (list/get/add), `repo` (list/get/add/delete), `release` (list/get/add/install/download), `analyze` (release/rdef), `svn-import-tags`, `test server`.
- **SVN Import subsystem** (`Infrastructure/Svn/`): 8 files implementing resumable SVN-to-BinStash migration with SQLite-backed state, concurrent file reads, inline streaming chunking. Fully implemented (not incomplete as previously stated).
- **Server communication:** `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC) in `BinStash.Cli/Infrastructure/`.
- **Local state:** SQLite database for auth tokens and server config; credentials protected via `Microsoft.AspNetCore.DataProtection`.
