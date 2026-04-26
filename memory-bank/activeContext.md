# Active Context

## Current work focus

As of 2026-04-25, a **major CLI-server release flow optimization and V6 rdef migration** was completed.

### Release flow optimization and V6 migration (completed 2026-04-25)

**Overview:** All CLI-server release creation flow bottlenecks identified and fixed. The StorageKey/ContentHash system was migrated to use FileHash directly as the pack store key (V6 rdef format).

**V6 rdef format:**
- `ReleasePackageSerializer.Version = 6`
- `§0x02` stores **FileHash** (BLAKE3 of file bytes), same semantics as V4
- V5 (StorageKey-based) was erased from codebase history; V5 deserializer retained for upgrade path only
- Pack store for file definitions keyed by **FileHash** instead of `BLAKE3(FileDefinitionRecord blob)`
- `FileDefinition.StorageKey` DB column dropped (EF migration `DropFileDefinitionStorageKey`)
- `FileDefinitionStorageKeyComputer` class deleted
- `FileDefinitionRecord.ComputeStorageKey()` removed
- `IndexedPackFileHandler` for fileDefs bucket: `_computeHash = data => FileDefinitionRecord.Deserialize(data).FileHash`
- Stats section (§0x0A) was not being written — fixed

**Network round trip reductions:**
- Removed redundant `GetRepositoryAsync` call (call #3) from `ReleaseAddOrchestrator` — chunker data already in `GetRepositoriesAsync` result
- Removed full release list scan (call #2) from `ReleaseAddOrchestrator` — server already enforces uniqueness at finalize (409 Conflict)
- Removed `GetFileDefinitionStorageKeysAsync` dependency from orchestrator — V6 uses ContentHash directly

**Performance fixes:**
- `IngestGrpcService.UploadChunks`: Per-chunk `AnyAsync` replaced with batched `WHERE Checksum IN (...)` query (batch size 64)
- `FastCdcChunker.ChunkUsingBuffer`: Replaced `ReadByte()` loop with 64KB block reads and a `MemoryStream` accumulator — eliminates per-byte syscalls and double-reads
- `BinStashGrpcClient.UploadChunksAsync`: Producer-consumer pipeline via `Channel<T>` (depth 4) overlaps disk I/O with gRPC stream writes
- `BinStashApiClient.CreateReleaseAsync`: .rdef serialization via `Pipe` — no intermediate `MemoryStream` buffer; serialize and send concurrently

**IngestSession improvements:**
- Added `IntendedRelease string?` field to `IngestSession` entity
- Server `CreateIngestSessionAsync` now accepts `CreateIngestSessionRequest` body and stores `IntendedRelease`
- EF migration `AddIngestSessionIntendedRelease` added

**Test results:** 337 tests pass (283 Core + 44 Serializers + 10 Server). Serializer tests updated: all `StorageKey` → `ContentHash`, version check `5 → 6`.

---

As of 2026-04-24, a **server codebase cleanup** pass was completed.

### Server cleanup (completed 2026-04-24)

**Build result:** 0 errors, 3 pre-existing warnings (CS8618 on `ChunkStore.cs`, CA2014 on `FastCdcChunker.cs`).

---

As of 2026-04-15, **BinStash.ChunkStoreExplorer** TUI utility has been **fully redesigned** as a file-explorer-style split-pane TUI. **Builds with 0 errors, 0 warnings.**

---

**Previously completed (2026-04-14):**
- BinStash.RepackFileDefs — cross-bucket FileDef repack tool
- BinStash.StoreMigration — one-shot migration tool for LSM-tree + self-keying format changes
- BINST-100 — chunk store rebuild as async background job
- BINST-99 — LSM-tree segmented pack-file index
- AOT fix — CredentialStore rewritten with Windows DPAPI / AES-256-GCM
- Release upgrade V1/V2 Length fix
- Frontend upgrade pipeline integration (graphql-ws, Apollo split link, ChunkStoreDetail.vue)
- BINST-93 — release upgrade pipeline rewrite

## Known discrepancies / items requiring attention

- **CliFx 3.0.0 migration incomplete:** CLI has LSP errors from API renames.
- **S3 chunk store not implemented.**
- **`chunk-store delete` and `release delete` commands throw `NotImplementedException`.**
- **Stale documentation:** `docs/faq.md` references ".NET 9"; `docs/file-format.md` covers V2/V3 only; `docs/architecture.md` is a placeholder.
- **`appsettings.Development.json`** uses `Email2` section, effectively disabling email in dev.
- **CLI `chunk-store show`** should display `BackendSettings` instead of `LocalPath`.
- **ChunkStoreExplorer** still references `StorageKey` on `OpaqueBlobBacking` (V5 compatibility path) — will display `(V5)` for old packs.

## Active decisions and preferences

- Deduplication scope is intentionally per-chunk-store.
- Auto-migration at startup; no manual `dotnet ef database update` in production.
- JWT key fallback must never reach production.
- **V6 `.rdef` is the current write format**; V1–V5 deserialization retained. V5 deserializer retained for upgrade path only.
- Artifacts sorted by path before V6 serialization — consumers must not assume positional stability.
- ChunkStore backend settings use JSON polymorphism (`$type` discriminator in `jsonb` column).
- Background jobs use polymorphic `BackgroundJob` entity with JSON payload columns.
- No SignalR — real-time updates use HotChocolate GraphQL subscriptions over WebSockets.
- Each background job pipeline uses a typed channel wrapper to avoid DI collision.
- **Pack store for file definitions is keyed by FileHash** (BLAKE3 of file bytes), not StorageKey (BLAKE3 of record blob).

## Important patterns observed

- All source files carry the AGPLv3 copyright header with author `Lukas Eßmann`.
- Authorization policies consistently named `Permission:<Scope>:<Level>`.
- `DbConfigurationSource` inserted at index 0 (lowest priority).
- CLI uses constructor DI via `ServiceCollection` in `Program.cs` `UseTypeInstantiator`.
- Ingestion pipeline supports plain directory and ZIP archive input.
- `ReleaseAddOrchestrator`, `ServerUploadPlanner`, `ComponentMapLoader` are CLI-side orchestration services.
- `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC) are server communication wrappers.
- SVN import subsystem (8 files) fully implemented with SQLite-backed resumable state.


### Server cleanup (completed 2026-04-24)

**Deleted/removed:**
- Empty `BinStash.Core/ChunkStreaming/` directory.
- `BinStash.Core/Ingestion/Formats/Zip/ZipFormatDetector.cs` (empty body).
- `BinStash.Server/HostedServices/SingleTenantBootstrapper.cs` (entire body was commented out).
- Stale `<Compile Remove="Ingestion\Models\StorageStrategy.cs" />` entry from `BinStash.Core.csproj`.
- Commented-out `GetReleaseStreamAsync` registration block and stale `// TODO: DELETE` comment from `ReleaseEndpoints.cs`.
- Commented-out duplicate `ChunkStoreShowCommand` block from `ReleaseCommands.cs`.
- Commented-out dead `ChunkStoreTestCommand` block from `ChunkStoreCommands.cs`.
- Commented-out `//builder.Services.AddHostedService<SingleTenantBootstrapper>();` from `Program.cs`.
- `//builder.Services.AddHostedService<ChunkStoreStatsHostedService>();` note remains — service was never registered; left as-is.

**REST/GraphQL boundary cleanup:**
- `TenantEndpoints`: Removed `POST /api/tenants` (create), `GET /api/tenants/{id}`, `PUT /api/tenants/{tenantId}` (update), `GET /api/tenants/current` — covered by GraphQL. Removed `CreateTenant`, `UpdateTenant`, `GetTenant`, `GetCurrentTenant` private handler methods. Fixed typo: `/current/invatations` → `/current/invitations`. Accidentally renamed `ListTenantsForMember` → `InviteMemberAsync` was corrected.
- `RepositoryEndpoints`: Removed entire tenant-prefixed `/api/tenants/{tenantId}/repositories/...` duplicate group. Fixed bug: `Name = chunkStore.Name` → `Name = repo.Name` in `CreateRepositoryAsync`.
- `ReleaseEndpoints`: Removed `GET /{id:guid}` (GraphQL handles reads); restored hardcoded download password with `// TEMPORARY:` comment.
- `ServiceAccountEndpoints`: Removed commented-out CRUD registrations and three dead private handler methods (`GetServiceAccountsAsync`, `CreateServiceAccountAsync`, `DeleteServiceAccountAsync`). Restored `using Microsoft.EntityFrameworkCore` (was accidentally removed, causing CS1061/CS1929 build errors).
- `TenantQueryService`: Removed duplicate `GetReleasesForRepository` method (identical logic in `RepositoryQueryService`).

**Other fixes:**
- `SetupGateMiddleware`: Now returns `application/problem+json` with `setup_required` error code instead of a plain string.
- `ReleasePackageSerializerBase.cs`: Removed commented-out local dict path.
- `ChunkStoreStatsHostedService`: Added `ILogger<ChunkStoreStatsHostedService>` injection; replaced silent `catch {}` with `catch (Exception ex) when (ex is not OperationCanceledException)` and `_logger.LogError(...)`.

**Build result:** 0 errors, 3 pre-existing warnings (CS8618 on `ChunkStore.cs`, CA2014 on `FastCdcChunker.cs`).

---

As of 2026-04-15, **BinStash.ChunkStoreExplorer** TUI utility has been **fully redesigned** as a file-explorer-style split-pane TUI (replacing the sequential CLI-style menu). New "Rebuild bloom filter" and "Rebuild segment file" write operations added. **Builds with 0 errors, 0 warnings.**

### BinStash.ChunkStoreExplorer — split-pane TUI redesign (completed 2026-04-15)

Previously a sequential CLI-style menu; now a file-explorer-style split-pane TUI with keyboard navigation, drill-down tree, and contextual action menus.

**Architecture:**
- Left panel: navigable tree `Store → Chunks/FileDefs → prefix-group-1 (e.g. "5") → prefix-group-2 (e.g. "5c") → bucket (e.g. "5c3") → files (pack/seg/bloom/log)`
- Right panel: stats and contextual information for the selected node
- Keyboard: `↑↓` move, `Enter`/`→` drill in, `Backspace`/`Esc`/`←` go up, `A` open action menu, `Q` quit
- `Console.ReadKey` manual loop with full-screen clear+re-render each keypress (no Spectre.Console SelectionPrompt owns the terminal)
- `ScanBucketCounts` runs once on startup; result reused for entire session

**Node model:**
- `ExplorerItem(Label, Detail, Tag)` record; tag types: `StoreTag`, `CategoryTag`, `PrefixGroup1Tag`, `PrefixGroup2Tag`, `BucketTag`, `PackFileTag`, `SegFileTag`, `BloomFileTag`, `LogFileTag`
- `ExplorerLevel` class: `Title`, `List<ExplorerItem> Items`, `int Cursor`
- Navigation: `Stack<ExplorerLevel>`

**New write operations (rebuild — atomic, safe while server is running):**
1. **Rebuild segment from packs** (`RebuildSegmentFromPacksAsync`) — scans all `.pack` files, decompresses blobs (Zstd), hashes with BLAKE3, sorts+deduplicates, writes new `seg-000.idx` via `SortedIndexSegment.WriteAsync`. Prompts for confirmation.
2. **Rebuild bloom filter (single segment)** (`RebuildBloomFilterForSegmentAsync`) — reads all entries from a segment via `SortedIndexSegment.ReadAllEntries()`, builds `PackIndexBloomFilter`, writes atomically.
3. **Rebuild all bloom filters in bucket** (`RebuildAllBloomFiltersInBucketAsync`) — iterates all segment files in bucket, rebuilds each bloom; option to skip existing blooms.

**All original features preserved:** Store Overview, Bucket Browser, Pack File Inspector, Segment File Inspector, Bloom Filter Inspector, FileDef Record Decoder, Hash Lookup, Integrity Check, Verify Pack Offsets, Dump Log Entries, Search Log, Read Raw Blob.

**File concurrency / FileShare rules (unchanged):**
- `.pack` files: `FileShare.ReadWrite`
- `.log` files: `FileShare.Read`
- `.seg-*.idx` files: `FileShare.ReadWrite | FileShare.Delete`
- `.bloom` files: `File.ReadAllBytes`

**Fix applied (2026-04-15):** `CS8803` compile error — type declarations (`record`s and `class ExplorerLevel`) were placed between top-level statements. Moved to end of file (after all top-level methods), which is the correct C# position.

**Build:** 0 errors, 0 warnings.

**Usage:**
```
BinStash.ChunkStoreExplorer <storeRoot>
# Example:
BinStash.ChunkStoreExplorer "C:\Tmp\BinStash\SecondLocalStoreSetup"
```

### BinStash.RepackFileDefs (completed 2026-04-14)

Cross-bucket repack tool that fixes the bucket-mismatch bug introduced by `BinStash.StoreMigration`. After `StoreMigration` ran, each FileDef pack entry was stored in the bucket corresponding to the **old file-hash prefix** (e.g., `aa8`) but `ObjectStore` routes reads by `storageKey = BLAKE3(blob)` prefix (e.g., `e86`). This caused all 10 `StoreVerify` tests to fail with `KeyNotFoundException`.

**Root cause (confirmed):** `StoreMigration.MigratePrefixAsync` iterates by old file-hash prefix `i.ToString("x3")` and writes new pack file and seg entries into `bucketDir = FileDefs/{oldPrefix[..2]}/` — the old prefix bucket. The `storageKey` is an independent BLAKE3 hash routing to a completely different bucket at read time.

**Fix:** `RepackFileDefs` does a cross-bucket redistribution:
1. Scans all 4096 existing `FileDefs` prefix directories and reads every pack entry.
2. Computes `storageKey = BLAKE3(decompressed_blob)` and derives `correctPrefix = storageKey.ToHexString()[..3]`.
3. Groups entries by `correctPrefix` and writes new `fileDefs{correctPrefix}-0.pack` files.
4. Deduplicates by `storageKey` within each target bucket (skips duplicate entries).
5. Deletes all stale seg/bloom/log index files.
6. Calls `ObjectStore.RebuildStorageAsync()` to regenerate all seg files from the correctly-routed pack files.

**Results on `C:\Tmp\BinStash\SecondLocalStoreSetup`:**
- 194,857 entries repacked across all 4,096 buckets in ~62s
- `RebuildStorageAsync()` returned OK
- `BinStash.StoreVerify` — **all 10 tests PASS**

**New files created:**
- `Utils/RepackFileDefs/RepackFileDefs.csproj` — console project, net10.0, references BinStash.Infrastructure + Blake3 2.2.1.
- `Utils/RepackFileDefs/Program.cs` — 4-step pipeline: scan → write correct packs → delete stale index → rebuild index.

**Infrastructure modified:**
- `BinStash.Infrastructure/BinStash.Infrastructure.csproj` — added `InternalsVisibleTo("BinStash.RepackFileDefs")`.

**Solution modified:**
- `BinStash.slnx` — `Utils/RepackFileDefs/RepackFileDefs.csproj` added to `/Tooling/` folder.

**Usage:**
```
BinStash.RepackFileDefs <storeRoot>
# Example:
BinStash.RepackFileDefs "C:\Tmp\BinStash\SecondLocalStoreSetup"
```

### FileDefinition retrieval call-site fix (completed 2026-04-14)

All server-side retrieval paths that previously passed `fileHash.ToHexString()` to the pack store (wrong) now look up `StorageKey` from the DB first and pass `storageKey.ToHexString()` (correct). The `ChecksumCompressor.TransposeDecompressHashes(blob)` calls that assumed the old raw-bytes format have been replaced with `FileDefinitionRecord.Deserialize(blob).ChunkHashes`.

**Files modified:**
- `BinStash.Server/Endpoints/ReleaseEndpoints.cs`:
  - Added `using BinStash.Infrastructure.Storage.FileDefinition;`
  - Primary download loop (`GetReleaseDownloadAsync`): pre-queries `(Checksum → StorageKey)` map from DB before `Task.WhenAll`, passes `storageKey.ToHexString()` to `RetrieveFileDefinitionAsync`, deserializes via `FileDefinitionRecord.Deserialize(blob).ChunkHashes`.
  - Diff-release download loop: identical fix with `diffStorageKeyMap`.
- `BinStash.Server/Endpoints/IngestSessionEndpoints.cs` (`FinalizeIngestSessionAsync`):
  - Pre-queries `(Checksum → StorageKey)` map from DB for `distinctContentHashes`.
  - Passes `fdStorageKeyMap.Values.Select(k => k.ToHexString())` to `RetrieveFileDefinitionsAsync`.
  - Iterates `fileDefinitionBytesByStorageKey` and deserializes via `FileDefinitionRecord.Deserialize(blob).ChunkHashes` to build `releaseUniqueChunks`.
  - The `storageKeyHexToFileHash` reverse map is built but available for future consumers; the `releaseUniqueChunks` HashSet only needs the chunk hashes (not the file-hash keying), so no further re-keying was required.

**Previously completed on 2026-04-14:** **BinStash.StoreMigration** standalone console project is **complete** and verified. Build is 0 errors. All 341 tests pass.

### BinStash.StoreMigration (completed 2026-04-14)

One-shot migration tool that repairs the existing on-disk FileDef store and PostgreSQL database after the BINST-99 LSM-tree index rewrite + BLAKE3-self-keying FileDef format changes.

**New files created:**
- `BinStash.StoreMigration/BinStash.StoreMigration.csproj` — console project, targets `net10.0`, references `BinStash.Infrastructure`, depends on `Npgsql` 10.0.2.
- `BinStash.StoreMigration/OldFlatIndexReader.cs` — reads old flat varint append-log index files (`index{prefix}.idx`): 32-byte hash + signed varint fileNo/offset/length per record.
- `BinStash.StoreMigration/Program.cs` — 5-step migration pipeline:
  1. `pg_dump` backup of PostgreSQL before changes.
  2. Parallel scan of all 4096 prefix buckets: reads old flat index, reads old pack payloads (raw `TransposeCompress(chunkHashes)`), queries DB for file lengths, re-serialises as `FileDefinitionRecord` blobs, writes new pack entries, builds new IDX2 sorted segment entries in memory.
  3. Writes new `fileDefs{prefix}.seg-000.idx` IDX2 segment files with correct `BLAKE3(blob)` keys.
  4. Deletes stale bloom filters (`fileDefs{prefix}.seg-000.bloom`), old flat index files (`index{prefix}.idx`), and legacy un-prefixed `seg-000.idx`/`seg-000.bloom` files.
  5. Bulk-updates `FileDefinition.StorageKey` in PostgreSQL (batched 500 rows, transactional).

**Infrastructure modified:**
- `BinStash.Infrastructure/BinStash.Infrastructure.csproj` — added `<AssemblyAttribute>` `InternalsVisibleTo("BinStash.StoreMigration")` to expose `PackFileEntry`, `SortedIndexSegment`, `IndexEntry`, `FileAtomicHelper`.

**Solution modified:**
- `BinStash.slnx` — `BinStash.StoreMigration` added to the `/Tooling/` solution folder.

**On-disk store structure confirmed:**
- Each bucket dir = `FileDefs/{xx}/` (first 2 hex digits of 3-hex prefix).
- 16 sub-prefixes per bucket (`000`–`00f` for bucket `00`, etc.) → 4096 total prefixes.
- Old files per prefix: `fileDefs{prefix}-0.pack` + `fileDefs{prefix}.seg-000.idx` (wrong-keyed) + `fileDefs{prefix}.seg-000.bloom` + `index{prefix}.idx` (source of truth).
- Migration: atomically replaces pack file, replaces seg-000.idx, deletes bloom, deletes old flat index.

**Usage:**
```
BinStash.StoreMigration <storeRoot> <connectionString>
# Example:
BinStash.StoreMigration "C:\Tmp\BinStash\SecondLocalStoreSetup" \
    "Host=localhost;Port=6432;Database=binstash;Username=postgres;Password=postgres"
```

Previously completed on 2026-04-14: **BINST-100 (migrate chunk store rebuild to background job system)** is **complete** and verified.

Previously completed on 2026-04-14: **BINST-99 (LSM-tree segmented pack-file index)** — monolithic `.idx` replaced with three-tier LSM-tree per prefix bucket.

## Recent changes (observed from code)

### BINST-100: Chunk store rebuild as async background job (completed 2026-04-14)

- **`BinStash.Core/Entities/BackgroundJob.cs`** updated — added `BackgroundJobTypes.ChunkStoreRebuild = "ChunkStoreRebuild"`, `ChunkStoreRebuildJobData` (with `ChunkStoreId`), `ChunkStoreRebuildProgressData` (with `TotalBuckets`, `ProcessedBuckets`, `FailedBuckets`).
- **`BinStash.Core/Storage/IChunkStoreStorage.cs`** — added `RebuildStorageWithProgressAsync(IProgress<bool> progress, CancellationToken ct)`.
- **`BinStash.Infrastructure/Storage/ObjectStore.cs`** — implemented `RebuildStorageWithProgressAsync`: sequential loop over 4096 prefixes × 2 categories, calls `progress.Report(bool)` after each bucket.
- **`BinStash.Infrastructure/Storage/LocalFolderChunkStoreStorage.cs`** — delegates `RebuildStorageWithProgressAsync` to `_objectStore`.
- **`BinStash.Server/Services/ChunkStores/IChunkStoreService.cs`** — added `RebuildStorageWithProgressAsync(ChunkStore store, IProgress<bool> progress, CancellationToken ct)`.
- **`BinStash.Server/Services/ChunkStores/ChunkStoreService.cs`** — implemented `RebuildStorageWithProgressAsync` delegating to storage.
- **`BinStash.Server/Services/ChunkStores/IChunkStoreRebuildService.cs`** — new interface with `ExecuteAsync(Guid jobId, CancellationToken ct)`.
- **`BinStash.Server/Services/ChunkStores/ChunkStoreRebuildService.cs`** — new service: runs 8192 bucket rebuild, broadcasts progress every 64 buckets via `ITopicEventSender`, checks DB cancellation flag at each broadcast interval, uses `LinkedCancellationTokenSource` for host-shutdown vs user-cancel separation. Inner `BroadcastingProgress : IProgress<bool>` chains async callbacks sequentially via `ContinueWith().Unwrap().GetAwaiter().GetResult()`.
- **`BinStash.Server/Services/ReleaseUpgrade/ReleaseUpgradeService.cs`** — `BackgroundJobProgressDto` extended with `TotalBuckets`, `ProcessedBuckets`, `FailedBuckets` (shared between upgrade and rebuild job types).
- **`BinStash.Server/HostedServices/RebuildJobChannel.cs`** — new typed wrapper around `Channel<Guid>` dedicated to rebuild jobs; prevents DI collision with the upgrade pipeline's plain `Channel<Guid>` singleton.
- **`BinStash.Server/HostedServices/ChunkStoreRebuildBackgroundService.cs`** — new hosted service: drains `RebuildJobChannel`, calls `IChunkStoreRebuildService.ExecuteAsync`, on startup re-enqueues any `ChunkStoreRebuild` jobs stuck in `Pending`/`Running` state (crash recovery).
- **`BinStash.Server/Endpoints/ChunkStoreEndpoints.cs`** — `RebuildChunkStoreAsync` replaced: now creates `BackgroundJob`, enqueues to `RebuildJobChannel`, returns `202 Accepted` with `RebuildJobDto`. Duplicate detection returns `409 Conflict`. Route registration updated from `Produces(200)` to `Produces<RebuildJobDto>(202)`.
- **`BinStash.Server/Endpoints/RebuildJobEndpoints.cs`** — new file: `GET /api/rebuild-jobs/{id}`, `POST /api/rebuild-jobs/{id}/cancel`, `GET /api/rebuild-jobs/` (with optional `chunkStoreId` filter). `RebuildJobDto` class defined here.
- **`BinStash.Server/Extensions/EndpointRouteBuilderExtensions.cs`** — added `app.MapRebuildJobEndpoints()` call.
- **`BinStash.Server/Program.cs`** — added `AddSingleton<RebuildJobChannel>()`, `AddScoped<IChunkStoreRebuildService, ChunkStoreRebuildService>()`, `AddHostedService<ChunkStoreRebuildBackgroundService>()`.
- **Bug fix: `BinStash.Server/Endpoints/IngestSessionEndpoints.cs`** — `GetMissingFileDefinitionsAsync` line 169: `ToList()` replaced with `ToListAsync()`.
- **Build:** 0 errors, 3 pre-existing warnings (unchanged). All 341 tests pass.

### BINST-99: LSM-tree segmented pack-file index (completed 2026-04-14)

- **`FileAtomicHelper.cs`** created — cross-platform atomic file replacement via write-to-temp-then-rename (`MoveFileExW` on Windows, `rename(2)` on POSIX).
- **`PackIndexBloomFilter.cs`** created — classic double-hashing bloom filter using first 16 bytes of BLAKE3 hash as h1/h2; target FPR 0.1%. On-disk: `[4 bytes bitCount][4 bytes hashCount][bitCount/8 bytes data]`.
- **`SortedIndexSegment.cs`** created — immutable memory-mapped sorted segment file (`seg-NNN.idx`). Fixed 48-byte records (32 hash + 4 fileNo + 8 offset + 4 length), header magic `0x58324449` ("IDX2"). Level encoded in filename. Binary search uses `stackalloc` 32-byte scratch buffer.
- **`IndexedPackFileHandler.cs`** fully rewritten — three-tier LSM-tree index: Tier 0 `index.log` + `_logDict`, Tier 1+ immutable sorted segments with bloom filters. Size-tiered compaction (16:1 fan-in, levels 0→1→2). All public method signatures unchanged.
- **`ObjectStore.cs`** updated — `GetStatisticsAsync()` uses lightweight stats path (8-byte segment headers + log hash replay). `CollectPrefixStatsLightAsync()` + helpers added.
- **Build:** 0 errors, 0 warnings. All 341 tests pass.

### AOT fix: CredentialStore DataProtection → Windows DPAPI / AES-GCM (completed 2026-04-14)

- Root cause: `XmlSerializer` in DataProtection's key ring management is trimmed under AOT.
- Fix: Pure BCL cryptography (`ProtectedData.Protect/Unprotect` on Windows; AES-256-GCM + HKDF-SHA256 on Linux/macOS).
- `auth.dat` format bumped to v2 (`BSA\x02` magic). Old v1 files silently discarded.
- Packages: Removed `Microsoft.AspNetCore.DataProtection` 10.0.5 and `.Extensions` 10.0.5. Added `System.Security.Cryptography.ProtectedData` 9.0.5.
- Verified: AOT publish produces 0 IL warnings. `auth list` works on AOT binary.

### Release upgrade V1/V2 Length fix (completed 2026-04-14)

- `PopulateMissingLengthsAsync()` added between deserialize and re-serialize steps.
- Batch-queries `FileDefinition` by `ContentHash` + `ChunkStoreId` to fill null `Length` fields.
- Handles both `OpaqueBlobBacking.Length` and `ContainerMemberBinding.Length`.

### Frontend upgrade pipeline integration (completed 2026-04-14)

- `graphql-ws` 6.0.8 installed; Apollo Client split link (HTTP + WebSocket).
- Vite proxy updated with `ws: true`.
- `upgradeChunkStore()` fixed (GET → POST), `UpgradeJobDto` + `ChunkStoreBackendSettingsDto` types added.
- `src/api/upgradeJobs.ts` created.
- `src/composables/useBackgroundJobProgress.ts` created (GraphQL subscription composable).
- `src/pages/ChunkStoreDetail.vue` created (full detail page with upgrade UI, real-time progress, tabs).
- `/chunk-stores/:id` route added. `ChunkStoreCard.vue` footer updated to "View Details".

### Release upgrade pipeline rewrite (BINST-93, backend complete)

- `BackgroundJob` entity created (polymorphic, `JobType` discriminator, `jsonb` payload columns).
- Old `ReleaseUpgradeJob` entity deleted. Migration `ReplaceReleaseUpgradeJobsWithBackgroundJobs` generated.
- `ReleaseUpgradeService` rewritten to use `ITopicEventSender` (HotChocolate).
- `ReleaseUpgradeBackgroundService` rewritten with `Channel<Guid>` job queue.
- `ChunkStoreEndpoints.cs` + `UpgradeJobEndpoints.cs` updated.
- `Subscription.cs` + `SubscriptionType.cs` created; `Program.cs` wired with WebSockets + in-memory subscriptions.

### Previous changes

- ChunkStore `LocalPath` replaced with polymorphic `BackendSettings` (`jsonb`, `[JsonPolymorphic]` with `$type`).
- `ChunkerOptions` enhanced with XML docs and `Validate()` method.
- CliFx upgraded to 3.0.0 (migration partially complete — LSP errors remain in CLI).
- V4 `.rdef` format with sort-by-path optimization (−30.4% vs V2).

## Known discrepancies / items requiring attention

- **BackendSettings JSON case sensitivity fixed:** `PropertyNameCaseInsensitive = true` added to handle existing PascalCase data.
- **CliFx 3.0.0 migration incomplete:** CLI has LSP/compile errors from API renames.
- **`StorageStrategy.cs` excluded from compilation.**
- **S3 chunk store not implemented.**
- **`SingleTenantBootstrapper` commented out.**
- **Stale documentation:** `docs/faq.md` references ".NET 9"; `docs/file-format.md` covers V2/V3 only; `docs/architecture.md` is a placeholder; `docs/cli-reference.md` missing several commands.
- **`chunk-store delete` and `release delete` commands throw `NotImplementedException`.**
- **`appsettings.Development.json`** uses `Email2` section, effectively disabling email in dev.
- **CLI `chunk-store show`** should display `BackendSettings` instead of `LocalPath`.

## Active decisions and preferences

- Deduplication scope is intentionally per-chunk-store.
- Auto-migration at startup; no manual `dotnet ef database update` in production.
- JWT key fallback must never reach production.
- V4 `.rdef` is the current write format; V1/V2/V3 deserialization retained.
- Artifacts sorted by path before V4 serialization — consumers must not assume positional stability.
- ChunkStore backend settings use JSON polymorphism (`$type` discriminator in `jsonb` column).
- Background jobs use polymorphic `BackgroundJob` entity with JSON payload columns.
- No SignalR — real-time updates use HotChocolate GraphQL subscriptions over WebSockets.
- Each background job pipeline uses a **typed channel wrapper** to avoid DI collision (e.g., `RebuildJobChannel` wraps the rebuild pipeline's `Channel<Guid>` separately from the upgrade pipeline's plain `Channel<Guid>` singleton).

## Important patterns observed

- All source files carry the AGPLv3 copyright header with author `Lukas Eßmann`.
- Authorization policies consistently named `Permission:<Scope>:<Level>`.
- `DbConfigurationSource` inserted at index 0 (lowest priority).
- CLI uses constructor DI via `ServiceCollection` in `Program.cs` `UseTypeInstantiator`.
- Ingestion pipeline supports plain directory and ZIP archive input.
- `ReleaseAddOrchestrator`, `ServerUploadPlanner`, `ComponentMapLoader` are CLI-side orchestration services.
- `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC) are server communication wrappers.
- SVN import subsystem (8 files) fully implemented with SQLite-backed resumable state.


Previously completed on 2026-04-14: the **release upgrade pipeline** (BINST-93 epic) backend work and its **frontend integration**. The Vue 3 frontend at `C:\Users\l.essmann\RiderProjects\Cruip\mosaic-vue` has been connected to the backend's async upgrade pipeline with real-time progress via GraphQL subscriptions over WebSocket.

**AOT fix completed (2026-04-14):** `CredentialStore` replaced `Microsoft.AspNetCore.DataProtection` with `System.Security.Cryptography.ProtectedData` (Windows DPAPI) + AES-256-GCM fallback (Linux/macOS). This resolves the `Value cannot be null (Parameter 'dictionary')` runtime crash in the AOT-published `win-x64` binary. Root cause was DataProtection's XML key ring management using `XmlSerializer` (reflection-based, trimmed under AOT). The new implementation is fully AOT-safe. Both `Microsoft.AspNetCore.DataProtection` and `Microsoft.AspNetCore.DataProtection.Extensions` packages removed from `BinStash.Cli.csproj`. `System.Security.Cryptography.ProtectedData` 9.0.5 added. The auth.dat file format was bumped to v2 (magic bytes `BSA\x02`) — old v1 DataProtection-encrypted files are gracefully discarded (forces re-login). AOT publish produces 0 IL warnings, 0 errors.

**Bug fix completed (2026-04-14):** `OpaqueBlobBacking.Length must be set before serialization` — the V4 serializer threw when upgrading V1/V2 releases because those formats did not store file lengths. Fixed by adding `PopulateMissingLengthsAsync()` in `ReleaseUpgradeService.ExecuteAsync()` which looks up null `Length` fields from the `FileDefinition` table (matching by `ContentHash`/`Checksum` + `ChunkStoreId`) before re-serialization. Both `OpaqueBlobBacking.Length` and `ContainerMemberBinding.Length` are handled.

Previously completed: BINST-91 (polymorphic ChunkStore backend settings) and BINST-92 (ChunkerOptions improvements).

## Recent changes (observed from code)

### BINST-99: LSM-tree segmented pack-file index (completed 2026-04-14)

- **`FileAtomicHelper.cs`** created at `BinStash.Infrastructure/Storage/Indexing/FileAtomicHelper.cs` — cross-platform atomic file replacement via write-to-temp-then-rename (`MoveFileExW` on Windows, `rename(2)` on POSIX).
- **`PackIndexBloomFilter.cs`** created at `BinStash.Infrastructure/Storage/Indexing/PackIndexBloomFilter.cs` — classic double-hashing bloom filter using first 16 bytes of BLAKE3 hash as h1/h2; target FPR 0.1%. On-disk: `[4 bytes bitCount][4 bytes hashCount][bitCount/8 bytes data]`.
- **`SortedIndexSegment.cs`** created at `BinStash.Infrastructure/Storage/Indexing/SortedIndexSegment.cs` — immutable memory-mapped sorted segment file (`seg-NNN.idx`). Fixed 48-byte records (32 hash + 4 fileNo + 8 offset + 4 length), header magic `0x58324449` ("IDX2"). Level encoded in filename (`seg-0NN` = L0 ≤65K, `seg-1NN` = L1 ≤1M, `seg-2NN` = L2 ≤16M). Binary search uses `stackalloc` 32-byte scratch buffer (zero heap on hot path). `ReadEntryCountFromHeader()` reads only 8 bytes for lightweight stats.
- **`IndexedPackFileHandler.cs`** fully rewritten at `BinStash.Infrastructure/Storage/Indexing/IndexedPackFileHandler.cs` — three-tier LSM-tree index:
  - Tier 0: `index.log` append log + `_logDict` ConcurrentDictionary (O(1) lookup). Flush to sorted segment at `LogFlushThreshold = 4096` entries.
  - Tier 1+: immutable sorted segments with paired bloom filters, newest-first traversal.
  - Size-tiered compaction: 16:1 fan-in, levels 0→1→2.
  - `RebuildIndexFile()` scans pack files, writes single sorted segment at correct level.
  - All public method signatures unchanged (transparent replacement).
- **`ObjectStore.cs`** updated:
  - `GetStatisticsAsync()` replaced with lightweight stats path: reads only 8-byte segment headers + replays log hashes (32 bytes/entry), no handler open.  `TotalCompressedSize`/`TotalUncompressedSize` remain 0 (intentional — would require opening every pack file).
  - `CollectPrefixStatsLightAsync()` + `CountLogEntries()` + `SkipVarInt()` helpers added.
- **Build:** 0 errors, 0 warnings. All 341 tests pass (283 Core + 48 Serializers + 10 Server).

### AOT fix: CredentialStore DataProtection → Windows DPAPI / AES-GCM (completed 2026-04-14)

- **Root cause:** `Microsoft.AspNetCore.DataProtection` uses XML-based key ring management internally (`XmlSerializer`) which relies on reflection and is trimmed/broken under AOT publish. The `ILLink.Substitutions.xml` file in DataProtection suppressed all IL2026/IL3050 warnings at publish time, making the failure silent until runtime.
- **Fix:** `CredentialStore.cs` rewritten to use `System.Security.Cryptography.ProtectedData.Protect/Unprotect` (Windows DPAPI) on Windows, and AES-256-GCM with HKDF-SHA256-derived key on Linux/macOS. Both paths are pure BCL cryptography — no XML serialization, no reflection, fully AOT-safe.
- **Format migration:** `auth.dat` now uses a `BSA\x02` magic prefix to identify the v2 format. If the file is missing the prefix (old v1 DataProtection format), it is silently discarded and the user is prompted to re-login.
- **Packages:** Removed `Microsoft.AspNetCore.DataProtection` 10.0.5 and `Microsoft.AspNetCore.DataProtection.Extensions` 10.0.5. Added `System.Security.Cryptography.ProtectedData` 9.0.5.
- **Verified:** AOT publish (`dotnet publish -r win-x64`) produces 0 IL warnings. `BinStash.Cli.exe auth list` runs cleanly on the AOT binary and returns "No authenticated servers."

### Release upgrade V1/V2 Length fix (completed 2026-04-14)

- **`ReleaseUpgradeService.ExecuteAsync()` fixed:** Added `PopulateMissingLengthsAsync()` static method between Step 2 (deserialize) and Step 3 (re-serialize). This method:
  1. Collects all `OpaqueBlobBacking` instances with `Length == null` and `ContentHash != null`
  2. Collects all `ContainerMemberBinding` instances with `Length == null` and `ContentHash != null`
  3. Batch-queries `FileDefinition` table by `ContentHash` + `ChunkStoreId` to get lengths
  4. Populates the null `Length` fields before V4 re-serialization
- **Root cause:** V1/V2 `.rdef` formats did not store file lengths. `OpaqueBlobBacking.Length` and `ContainerMemberBinding.Length` were null after deserialization. The V4 serializer (`ReleasePackageSerializer.cs` lines 214-215 and 128-129) requires both to be non-null.
- **Pattern follows existing code:** Same lookup pattern used in `IngestSessionEndpoints.CalculateOpaqueArtifactSize()` (line 671-679) and `ChunkStoreStatsCollector` (lines 238-244, 338-346).
- **Build:** 0 errors, all 341 tests pass (283 Core + 48 Serializers + 10 Server).

### Frontend upgrade pipeline integration (completed 2026-04-14)

- **`graphql-ws` 6.0.8 installed** via pnpm — WebSocket transport for Apollo Client subscriptions.
- **Vite proxy updated** — `ws: true` added to `/graphql` proxy in `vite.config.js` to enable WebSocket proxying for subscriptions.
- **`apolloClient.ts` rewritten** — Split link architecture: `HttpLink` for queries/mutations, `GraphQLWsLink` (via `graphql-ws`) for subscriptions. Auto-detects `ws://` vs `wss://` based on page protocol.
- **`upgradeChunkStore()` fixed** in `src/api/chunkStores.ts` — Changed from GET to POST, now returns `UpgradeJobDto` from the 202 response.
- **`UpgradeJobDto` type added** to `src/api/chunkStores.ts`.
- **`ChunkStoreBackendSettingsDto` type added** and `ChunkStoreDetailDto` updated to include `backendSettings`.
- **`src/api/upgradeJobs.ts` created** — REST API functions: `getUpgradeJob(id)`, `cancelUpgradeJob(id)`, `listUpgradeJobs(chunkStoreId?)`.
- **`src/composables/useBackgroundJobProgress.ts` created** — Composable using `apolloClient.subscribe()` with `GraphQLWsLink` for `backgroundJobProgress(jobId: UUID!)` subscription. Exposes reactive `progress`, `error`, `isSubscribed` refs plus `subscribe(jobId)` and `unsubscribe()` methods. Auto-unsubscribes on terminal states and component unmount.
- **`src/pages/ChunkStoreDetail.vue` created** — Full detail page with:
  - Breadcrumb navigation back to `/chunk-stores`
  - Header card with chunk store name, type, backend settings path
  - "Upgrade Releases" button that calls `upgradeChunkStore(id)` and subscribes to progress
  - Real-time upgrade progress panel (progress bar, processed/total/failed/skipped counts, bytes saved, status badge, cancel button)
  - Tabs: Overview (store details, chunker config, stats) and Upgrade Jobs (job history list)
  - Loading/error states following existing patterns
- **Route added** — `/chunk-stores/:id` route added to `src/app/router/index.ts` importing `ChunkStoreDetail.vue`.
- **`ChunkStoreCard.vue` cleaned up** — Footer changed from "Send Message" (linking to `/messages`) to "View Details" (linking to the chunk store's detail page).

### Release upgrade pipeline rewrite (BINST-93, backend complete)

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
- **`SingleTenantBootstrapper` removed:** Dead hosted service (`SingleTenantBootstrapper.cs`) and its commented-out registration in `Program.cs` have been deleted. Single-tenant init relies solely on `SetupBootstrapper`.
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
