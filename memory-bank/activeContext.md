# Active Context

## Current work focus

As of 2026-04-13, implemented the V4 `.rdef` release package format, completing the efficiency improvement evaluation that was in progress.

## Recent changes (observed from code)

- Target framework upgraded to `net10.0` across all projects (Dockerfile still references `aspnet:9.0` / `sdk:9.0` — this is a discrepancy to be aware of).
- `BinStash.Core` has a file excluded from compilation: `Ingestion/Models/StorageStrategy.cs` (removed via `<Compile Remove=...>`).
- `SingleTenantBootstrapper` hosted service is registered but commented out in `Program.cs`.
- Scalar API reference UI is wired for Development mode (`/scalar`).
- **V4 release format implemented (2026-04-13):** `ReleasePackageSerializer` bumped to `Version = 4`. Token-based path encoding replaces full-path string table entries. See systemPatterns.md for details.

## Known discrepancies / items to verify

- **Dockerfile base image mismatch:** `BinStash.Server/Dockerfile` uses `mcr.microsoft.com/dotnet/aspnet:9.0` but the project targets `net10.0`. This will fail to build correctly until updated to `aspnet:10.0` / `sdk:10.0`.
- **README badge** says `.NET 9.0` but `csproj` files target `net10.0`.
- `StorageStrategy.cs` is excluded from compilation — its role is unclear; do not reference it.
- S3 chunk store type is mentioned in CLI help text and server README but no implementation class exists yet.

## Next steps (inferred)

- Fix Dockerfile to use .NET 10 base images.
- Implement S3 chunk store storage backend.
- Complete CLI command coverage (SVN import command exists as `SvnImportTagsCommand.cs`).
- Tier 1 unit tests implemented (2026-04-13): `Hash32Specs`, `Hash8Specs`, `BytesConverterSpecs`, `ZipMemberSelectionPolicySpecs`, `DictionaryExtensionsSpecs`, `BoundedStreamSpecs`, `BitReaderSpecs`, `ByteArrayComparerSpecs`, `StreamExtensionsSpecs`. All 227 tests passed.
- `BinStash.Core/Properties/AssemblyInfo.cs` added with `[assembly: InternalsVisibleTo("BinStash.Core.Tests")]` to enable testing of `BoundedStream`, `BitReader`, `ByteArrayComparer`.
- Tier 2 unit tests implemented (2026-04-13): `ChecksumCompressorSpecs` (22 tests) and `ZipReconstructionPlannerSpecs` (34 tests). Total after tier 2: 283 tests passing. `CanonicalNodes` tests excluded per request.
- Bug fix applied (2026-04-13): `SubstringTableBuilder.Tokenize` in `BinStash.Core/Serialization/Utils/SubstringTableBuilder.cs` — removed `if (i > start)` guard so empty segments from consecutive separators (e.g. `://` in URLs) are no longer silently dropped. The bug caused V2 deserialization to reconstruct `https://…` as `https:…`.
- Tier 3 tests implemented (2026-04-13): `ReleasePackageSerializerSpecs` (39 new tests in `BinStash.Serializers.Tests`) covering V3 header magic/version, metadata round-trips, stats, custom properties (including URL values with `://` and `file:///` triple-slash), opaque artifact round-trips, reconstructed container round-trips, mixed artifacts, compression options, and error cases. `SubstringTableBuilderSpecs` (21 tests in `BinStash.Core.Tests`) directly testing the URL bug fix and all separator edge cases. Total: 343 tests passing.
- **V4 format implemented (2026-04-13):** Token-based string table, `ComponentName` derived from path, 7 new V4-specific tests added. Total: 351 tests passing.
  - **Artifact sort-by-path implemented in V4 (2026-04-13):** `SerializeAsync` now sorts `OutputArtifacts` by path (ordinal) before writing §0x05 and §0x06. Adjacent artifacts share path-token prefix runs, giving Zstd far better locality. Result on the 11,049-artifact real sample: §0x05 29,924 B → 21,168 B, §0x06 26,267 B → 17,277 B, total V4 **178,795 B → 161,049 B (−30.4% vs V2)**. Two tests updated to look up artifacts by path (dict) instead of by position, since sort order is now an encoding detail, not a semantic contract. All 331 tests passing.
  - **Format experiments closed (2026-04-13, 9 experiments):** Cross-section concat (EXP 6), naive §0x05+§0x06 merge (EXP 3), delta-hash-index (EXP 5), outer Zstd passthrough for §0x02 (EXP 2) — all rejected by measurement. BackingType, Kind, RequiresBytePerfectReconstruction bytes kept per-artifact (user confirmed they can all differ). Frequency-sorted token table also previously rejected. Sort-by-path (EXP 1/4/5 combined) is the confirmed winner.

## Active decisions and preferences

- Deduplication scope is intentionally per-chunk-store, not global.
- Auto-migration at startup is the chosen migration strategy; no manual `dotnet ef database update` in production.
- JWT key fallback (`"dev-only-change-me"`) must never reach production — flagged in code comments.

## Important patterns observed

- All source files carry the AGPLv3 copyright header with author `Lukas Eßmann`.
- Authorization policies are consistently named `Permission:<Scope>:<Level>`.
- `DbConfigurationSource` is inserted at index 0 (lowest priority) so database-stored settings are always overridable.
- CLI uses constructor DI via `ServiceCollection` in `Program.cs` `UseTypeActivator`; commands are transient, services are singletons.
- Ingestion pipeline in Core supports two input formats: plain directory (`PlainFileFormatHandler`) and ZIP archives (`ZipFormatHandler`).
- `ReleaseAddOrchestrator`, `ServerUploadPlanner`, and `ComponentMapLoader` are the CLI-side orchestration services for `release add`.
- `BinStashApiClient` (REST) and `BinStashGrpcClient` (gRPC) in `BinStash.Cli/Infrastructure/` are the server communication wrappers.
