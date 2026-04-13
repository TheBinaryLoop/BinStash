# Active Context

## Current work focus

As of 2026-04-13, Memory Bank fully initialized from code inspection. No active feature branch has been identified. The project is in alpha status.

## Recent changes (observed from code)

- Target framework upgraded to `net10.0` across all projects (Dockerfile still references `aspnet:9.0` / `sdk:9.0` — this is a discrepancy to be aware of).
- `BinStash.Core` has a file excluded from compilation: `Ingestion/Models/StorageStrategy.cs` (removed via `<Compile Remove=...>`).
- `SingleTenantBootstrapper` hosted service is registered but commented out in `Program.cs`.
- Scalar API reference UI is wired for Development mode (`/scalar`).

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
- Tier 2 unit tests implemented (2026-04-13): `ChecksumCompressorSpecs` (22 tests covering empty list, wrong hash size, all three decompression overloads sync/async, order preservation, large list round-trip) and `ZipReconstructionPlannerSpecs` (34 tests covering `.apk`/`.jar`/`.nupkg` byte-perfect detection, case-insensitivity, empty/directory-only/policy-filtered entries producing opaque storage, semantic reconstruction with entry filtering, reason string non-nullness). Total: 283 tests passing. `CanonicalNodes` tests excluded per request.
- Next test tier: serializer V3 round-trip restore.

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
