# System Patterns

## Purpose
- Capture durable architecture, integration, and design patterns verified in this repository.
- Help future agents change code consistently with existing system behavior.

## What does not belong here
- unverified architecture speculation
- detailed technology setup that belongs in `techContext.md`
- unresolved blind spots that belong in `knownGaps.md`

## Architecture Style
- Layered **modular monolith**: one deployable server + a separate CLI + a static Vue SPA bundled into the server. No microservices.
- Project dependency rule: `Contracts` (no internal deps) ← `Core` ← `Infrastructure` ← `Server`; `Cli` depends on `Contracts` + `Core` only. **`Core` must never reference `Infrastructure` or `Server`; `Contracts` references nothing internal.** Core defines abstractions (e.g. `IChunkStoreStorage`, `IChunkStoreStorageFactory`, billing interfaces); Infrastructure/Server implement them.

## Module Or Service Boundaries
- `BinStash.Contracts` — DTOs/wire contracts + gRPC `Protos/ingest.proto`.
- `BinStash.Core` — domain + algorithms: `Chunking` (custom FastCDC), `Compression` (Zstd via `ZstdNetNGX`, transpose hash compression), `Serialization` (`.rdef`/`.pack`/`FileDefinitionRecord`), `Ingestion`, `Storage` (abstractions), `Entities`, `Auth` (enums/interfaces), `Billing` (interfaces + NoOp).
- `BinStash.Infrastructure` — persistence + IO: `Data` (`BinStashDbContext` + migrations), `Storage` (pack files, LSM index), email template rendering.
- `BinStash.Server` — API host (auth, GraphQL, gRPC, REST, hosted services, billing plugin loader, config layering).
- `BinStash.Cli` — CliFx client (commands, REST+gRPC clients, release-add orchestration, credential store, SVN import).
- `BinStash.Frontend` — Vue SPA (Apollo GraphQL + REST).

## Core Data Flow Patterns
- **Ingestion pipeline (CLI → server)**: `IInputDiscoveryService` → `IInputFormatDetector` (extension-based: container / compression-wrapper / opaque) → `IIngestionPlanner` → `IInputFormatHandler` (`PlainFileFormatHandler`, `ZipFormatHandler` w/ deterministic reconstruction). Files are chunked by `FastCdcChunker` (own seeded gear-hash; memory-mapped / buffered / streaming paths) and hashed with **BLAKE3** → `Hash32`. The CLI streams only-missing chunks and file definitions over gRPC (`UploadChunks` / `UploadFileDefinitions`), then finalizes via REST; the release is recorded as a `.rdef`.
- **Storage**: `ObjectStore` (per base path, via `ObjectStoreManager`) lays out `Chunks/` and `FileDefs/` in 4096 3-hex-prefix buckets plus `Releases/*.rdef`. Chunks key on BLAKE3; file definitions key on the embedded `FileHash` (content identity). Pack entries use a 3-tier **LSM index** (append log → sorted memory-mapped `IDX2` segments + bloom filters, size-tiered compaction). Pack files rotate at 4 GiB; writes use atomic temp-then-rename.
- **Binary metadata format**: `ReleasePackageSerializer` writes `.rdef` (magic `BPKG`, current write version **V6**, reads V1–V6); see `dataModelNotes.md` for the section/versioning detail. This format is load-bearing — preserve backward compatibility or migrate via `BinStash.StoreMigration`.

## Integration Patterns
- **GraphQL (HotChocolate 15.1.14)** is the primary management surface (resource reads + CRUD/mutations), with filtering/sorting/projections and paged `IncludeTotalCount`; cost limits disabled. **gRPC** is the high-throughput ingest path. **REST** handles auth/identity, ingest sessions, first-run setup, instance/tenant admin, releases download, and a public `GET /api/instance/config`. The split is deliberate: GraphQL for queries/CRUD, REST for auth/ingest/setup.
- **Frontend ↔ server**: Apollo split-link — `HttpLink` to `/graphql` for queries/mutations, `graphql-ws` WebSocket for subscriptions — alongside plain `fetch` to `/api/*` and `/health`. Frontend builds into `BinStash.Server/wwwroot`; server serves the SPA via `MapFallbackToFile("index.html")`.
- **Billing plugin boundary**: `BinStash.Core/Billing` defines `IBillingProvider`, `IBillingLimits`, `IUsageMeteringService`, `IBillingPluginRegistrar` with NoOp defaults (unlimited). The server registers NoOp first, then `BillingPluginLoader` optionally `Assembly.LoadFrom`s a commercial plugin **only** from env var `BINSTASH_BILLING_PLUGIN_PATH` (never from DB config) and lets it override registrations + map endpoints.

## Consistency, Reliability, And Error Handling
- **Background-job pattern**: a single polymorphic `BackgroundJob` entity (`JobType` discriminator + `jsonb` `JobData`/`ProgressData`/`ErrorDetails`, `Pending→Running→Completed/Failed/Cancelled`). Work is queued on unbounded single-reader `Channel<Guid>`s; the chunk-store-rebuild pipeline uses a **typed `RebuildJobChannel` wrapper** to avoid DI collision with the release-upgrade pipeline. Hosted services drain one job at a time, scope per job, and **resume `Pending`/`Running` jobs on startup** (crash recovery). Progress is broadcast via HotChocolate `ITopicEventSender` on topic `BackgroundJobProgress_{jobId}`.
- **Configuration layering**: `DbConfigurationSource` (reads/writes the `InstanceSettings` table) is inserted at **index 0 (lowest priority)** so DB-backed settings are always overridable by appsettings/env/user-secrets.
- **DB migrations auto-apply at startup** via `db.Database.Migrate()` (no manual `dotnet ef database update` in production).

## Security And Access Patterns
- **"Smart" composite auth**: a policy scheme whose `ForwardDefaultSelector` routes by `Authorization` prefix — `Bearer ` → JWT (HS256), `ApiKey ` → `ApiKeyAuthHandler` (parses `ApiKey {guid}.{secret}`, `PasswordHasher`-verified), else → Identity cookie. Service accounts authenticate **via** API keys. A separate short-lived `Setup` cookie scheme guards first-run.
- **Authorization** uses `Permission:<Scope>:<Level>` policies across three scopes — Instance (`Admin`), Tenant (`Admin`/`BillingAdmin`/`Member`), Repository (`Admin`/`Write`/`Read`) — resolved by resource-based handlers with a cascade (instance-admin → tenant membership/admin → direct user repo-role → group repo-role).
- **Multi-tenancy**: `TenancySettings.Mode` (`Single`|`Multi`); `TenantResolutionMiddleware` resolves the tenant (route → query → `X-Tenant-Id` → subdomain in Multi mode), carried in scoped `ITenantContext`. gRPC ingest enforces `RepositoryPermission.Write` and meters usage.
- **License boundary is security-relevant**: no commercial/metered logic in Core (verified — no Stripe/subscription/invoice references); plugin path comes only from env.

## Testing Patterns
- xUnit 2 + FluentAssertions across `BinStash.Core.Tests`, `BinStash.Serializers.Tests`, `BinStash.Server.Tests`.
- `Core.Tests` adds **FsCheck** (property-based) + **Verify** (snapshot), and references `BinStash.Server` (build-only) to exercise the reflection-loaded billing boundary. `Serializers.Tests` uses **Verify** round-trip tests over embedded `.rdef` samples (the binary-format safety net). `Server.Tests` uses **`Mvc.Testing` + EF Core InMemory**.
- No integration / end-to-end tests exist.

## Source Notes
- Verified from code across all six first-class projects (see file-specific references in `serviceInventory.md` / `interfaceInventory.md` / `dataModelNotes.md`). Build/tests not executed this pass.
