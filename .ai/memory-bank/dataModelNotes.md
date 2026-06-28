# Data Model Notes

## Purpose
- Capture the load-bearing persistence and storage constraints: the PostgreSQL schema discipline and the versioned on-disk binary formats.
- Help future agents avoid breaking data semantics or backward compatibility.

## What does not belong here
- full schema dumps or generated migration content
- transient data-cleanup notes
- API contract detail (see `interfaceInventory.md`)

## Storage Overview
- **Two stores**: (1) PostgreSQL for relational metadata (`BinStashDbContext`); (2) an on-disk content-addressed object store per chunk store (`LocalFolder` backend → `ObjectStore`).
- On-disk layout under a chunk store's base path: `Chunks/<2-hex>/`, `FileDefs/<2-hex>/`, `Releases/<3-hex>/*.rdef`. A 3-hex prefix yields **4096 buckets**. Chunks are keyed by BLAKE3; file definitions by the embedded `FileHash` (content identity); releases by id.

## Important Entities Or Collections
- `BinStashDbContext : IdentityDbContext<BinStashUser, IdentityRole<Guid>, Guid>` with **25 DbSets**: ApiKeys, Chunks, ChunkStores, ChunkStoreStatsSnapshots, FileDefinitions, IngestSessions, Releases, ReleaseMetrics, Repositories, RepositoryRoleAssignments, ServiceAccounts, StorageClasses/StorageClassMappings/StorageClassDefaultMappings, Tenants/TenantMembers/TenantMemberInvitations/TenantRoleAssignments, UserGroups/UserGroupMembers, UserRefreshTokens, InstanceSettings, SetupStates, SetupCodes, BackgroundJobs.
- **Polymorphic columns (PostgreSQL `jsonb`)**: `ChunkStore.BackendSettings` (`ChunkStoreBackendSettings`, `[JsonPolymorphic("$type")]`, only `LocalFolderBackendSettings` today) and `BackgroundJob` (`JobType` discriminator + `JobData`/`ProgressData`/`ErrorDetails`). `ChunkerOptions` is an EF **owned** type (`Chunker_*` columns). The `Subscription` entity was **removed** (migration `RemoveSubscriptionEntity`).

## Ownership And Consistency Rules
- **Migrations**: exactly **36** in `Infrastructure/Data/Migrations` (most recent `…RemoveSubscriptionEntity`); they **auto-apply at startup** via `db.Database.Migrate()`. **Do not hand-edit existing migrations** — generate new ones (`dotnet ef migrations add <Name> --project BinStash.Infrastructure --startup-project BinStash.Server`). Schema is PostgreSQL-specific (`Npgsql`).
- `ChunkStore.BackendSettings` JSON is read case-insensitively (`PropertyNameCaseInsensitive = true`, camelCase writes) so legacy PascalCase data still deserializes — keep that when touching the converter.
- Deduplication is **per chunk store** (no cross-store dedup).

## On-Disk Format Constraints (preserve backward compatibility)
- **`.rdef`** (`ReleasePackageSerializer`): magic `BPKG`, 6-byte header (magic + version + flags), current write **V6**, reads V1–V6. Sections are `[id][flags][varint len][payload]`, each optionally Zstd-compressed: `0x01` metadata, `0x02` content-hash table (transpose-compressed BLAKE3), `0x03` path-**segment** token table (byte-sorted), `0x04` custom properties, `0x05` output artifacts (path as token-index list; **backing index is implicit by ordinal position per backing type**), `0x06` opaque backings, `0x07` reconstructed-container backings, `0x08` container members, `0x09` recipe payloads. Artifacts are sorted by path (Ordinal) before write. **Critical keying invariant**: §0x02 holds **`FileHash`** (BLAKE3 of file bytes) in **V4 and V6**, but **`storageKey`** (BLAKE3 of the record blob) in **V5** — never conflate them; this drives object-store lookups.
- **`.pack` entry** (`PackFileEntry`): magic `BSPK` (LE `0x4B505342`), version 1, **21-byte header** (magic | version | uncompressed len `u32` | compressed len `u32` | xxHash3 checksum `u64`), body Zstd-compressed. Checksum is **xxHash3 over the compressed bytes**. Pack files rotate at **4 GiB**; lengths are `u32`, so per-entry size is structurally capped below 4 GiB.
- **`FileDefinitionRecord`**: magic `BSFD` (LE), version 1, 49-byte fixed header (`FileHash` + `FileLength i64` + `ChunkCount i32`), then transpose-compressed chunk hashes. Object-store index key is the embedded `FileHash`.
- **LSM index** (`Infrastructure/Storage/Indexing`): tier-0 append `.log` (32-byte hash + 3 varints), flushed at 4096 entries into immutable sorted `IDX2` segments (`seg-LNN.idx`, magic `0x58324449`, 8-byte header, **48-byte records** = 32 hash + `u32` fileNo + `u64` offset + `u32` len, ascending → mmap binary search) with paired `.bloom` filters (double-hash from first 16 bytes of BLAKE3, ~0.1% FPR). Size-tiered compaction (fan-in 16, levels 0→1→2). Segment writes are atomic (temp-then-rename + fsync). The `IDX2` magic + 48-byte width + sort order are load-bearing.
- **Transpose compression** (`Core/Compression/ChecksumCompressor`): N×32-byte hashes are transposed into 32 columns, each Zstd-compressed (level 9) independently; backs `.rdef` §0x02 and `FileDefinitionRecord` chunk hashes.

## Migration Or Compatibility Notes
- Format/schema changes must preserve read compatibility or go through `BinStash.StoreMigration` (note: that tool's `.csproj` is not currently in `BinStash.slnx` — verify currency before relying on it).
- The custom Zstd fork is **`ZstdNetNGX`** — do not substitute upstream ZstdNet.

## Source Notes
- Verified from `ReleasePackageSerializer.cs`, `PackFileEntry.cs`, `FileDefinitionRecord.cs`, `SortedIndexSegment.cs`, `IndexedPackFileHandler.cs`, `ObjectStore.cs`, `ChecksumCompressor.cs`, `BinStashDbContext.cs`, the `ChunkStore` entity config, and the `Data/Migrations` listing. Build/tests not executed.
