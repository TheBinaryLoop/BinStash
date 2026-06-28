# Interface Inventory

## Purpose
- Capture the system boundaries future agents must not break: network APIs, the ingest RPC, the on-disk file-format contracts, and the billing plugin seam.
- Point at where each contract is defined.

## What does not belong here
- internal implementation detail (see `systemPatterns.md`)
- unverified contract assumptions
- transient endpoint lists from debugging

## Scope And Confidence
- Representative of the externally- and cross-process-visible interfaces, verified in code. Field-level GraphQL/REST schemas are not exhaustively enumerated here.

## Interfaces

### GraphQL management API
- Type: HTTP GraphQL (HotChocolate 15.1.14) at `/graphql`; the **primary management surface** (resource reads + CRUD mutations), with filtering/sorting/projections and paged `IncludeTotalCount`; cost limits disabled.
- Producer: `BinStash.Server/GraphQL` (`QueryType`, `MutationType`). Consumers: `BinStash.Frontend` (Apollo) and the CLI (reads).
- Notes: subscription transport (`graphql-ws` over WebSockets) is wired (`AddInMemorySubscriptions`/`UseWebSockets`) but **no `Subscription` root type is registered** — see `knownGaps.md`.

### gRPC ingest
- Type: gRPC streaming. Service `IngestService` (`BinStash.Contracts/Protos/ingest.proto`): `UploadChunks(stream …)` and `UploadFileDefinitions(stream …)`.
- Producer: CLI (`BinStashGrpcClient`, `GrpcServices=Client`). Consumer: `Server/Grpc/IngestGrpcService.cs` (`GrpcServices=Server`).
- Reliability/contract: requires `x-ingest-session-id` / `x-repo-id` metadata, enforces `RepositoryPermission.Write`, BLAKE3-verifies chunks, batches existence checks (size 64), meters ingest. **Proto field numbers must stay backward-compatible** — both sides compile this one file.

### REST API
- Type: HTTP REST (minimal-API endpoint groups in `Server/Endpoints`, wired by `Extensions/EndpointRouteBuilderExtensions.cs`). Handles what GraphQL does not: auth, ingest, first-run setup, instance/tenant admin.
- Groups: `IdentityEndpoints` (`/api/auth` register/login/logout/refresh/confirm/reset/machine-token), `IngestSessionEndpoints` (`/api/tenants/{tenantId}/repositories/{repoId}/ingest` sessions/files/chunks/finalize, `Repo:Write`, **402** when ingest not allowed), `ChunkStoreEndpoints` (`/api/chunk-stores`, `Instance:Admin`, incl. rebuild/upgrade → `202`), `ReleaseEndpoints` (`…/releases` download, `Repo:Read`, egress-metered), `RepositoryEndpoints` (`/api/repositories`), `ServiceAccountEndpoints` (API-key mgmt), `StorageClassEndpoints`, `TenantEndpoints` (members/invitations), `InstanceEndpoints` (stats/config + public `GET /api/instance/config`), `SetupEndpoints` (first-run), health at `/health` (`Instance:Admin`).
- Consumers: CLI (auth/ingest/finalize/download) and the frontend (`/api/*`, `/health`).

### On-disk file-format contracts (versioned)
- `.rdef` release definition — magic `BPKG`, write **V6**, reads V1–V6 (`ReleasePackageSerializer`).
- `.pack` entry — magic `BSPK` v1, 21-byte header, xxHash3-over-compressed checksum, 4 GiB rotation.
- `FileDefinitionRecord` — magic `BSFD` v1, content-hash (`FileHash`) keyed.
- Index segment — magic `IDX2`, 48-byte sorted records; `.bloom` sidecars; `.log` append tier.
- Contract rule: these are cross-version and cross-process; preserve backward compatibility or migrate via `BinStash.StoreMigration`. Detail in `dataModelNotes.md`.

### Billing plugin seam
- Type: in-process assembly plugin. Contract `IBillingPluginRegistrar` (`Register(IServiceCollection, IConfiguration)` + `MapEndpoints`) plus `IBillingProvider` / `IBillingLimits` / `IUsageMeteringService` (`BinStash.Core/Billing`).
- Loader: `Server/Billing/BillingPluginLoader` — `Assembly.LoadFrom` a path from env var `BINSTASH_BILLING_PLUGIN_PATH` **only** (never DB config); absent → NoOp (unlimited). The plugin override the NoOp registrations and may map its own endpoints.

### Outbound: email
- Type: HTTP to Brevo (`POST https://api.brevo.com/v3/smtp/email`, `api-key` header) via `Server/Email/Providers/BrevoEmailProvider`; bodies rendered from Handlebars `.hbs` templates embedded in `BinStash.Infrastructure`.

### Frontend ↔ server transport
- Apollo split-link: `HttpLink` → `/graphql` (queries/mutations, `credentials: include`); `graphql-ws` → `ws(s)://…/graphql` (subscriptions). Plus `fetch` to `/api/*` and `/health`. Dev server proxies these to `https://localhost:7117`.
