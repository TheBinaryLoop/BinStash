# AGENTS.md

This file provides instructions for coding agents working in this repository.

`README.md` files are for human contributors.  
`AGENTS.md` complements them with agent-specific workflow, repository rules, and navigation to maintained project knowledge that should not clutter human-facing READMEs.

## Mandatory startup workflow

These steps are mandatory for every session:

1. Read `AGENTS.md` fully.
2. Read every Markdown file directly under `memory-bank/` before doing substantive task work.
3. Treat those root Memory Bank files as the required baseline context for the session.
4. Read files in Memory Bank subdirectories when the task or the root Memory Bank files indicate that they are relevant.
5. If a detail is runtime-critical, verify it in the code of the concrete service, module, or application being changed.
6. If the user says `update memory bank`, re-read all root Memory Bank files and all relevant supporting Memory Bank files before editing them.

Do not skip or postpone the Memory Bank read. In this repository, the Memory Bank is not optional background reading; it is part of the required operating procedure for agents.

### Practical subdirectory rule

- Root-level Memory Bank files are mandatory reading for every session.
- Subdirectory files are mandatory when the task clearly touches their subject area.
- For domain-, customer-, tenant-, partner-, or environment-specific tasks, start with the relevant index file in `memory-bank/` and then read the referenced files in the related subdirectories.
- For deep dives, follow the references from the root Memory Bank files instead of blindly reading unrelated subdirectories.

## Source-of-truth and precedence rules

- `AGENTS.md` defines how agents should work in this repository.
- `memory-bank/` is the primary source of maintained project context and repository knowledge.
- If `AGENTS.md` and the Memory Bank differ on project detail, prefer the Memory Bank.
- If documentation and runtime behavior differ, verify and follow the code for the touched service, module, or application.
- Preserve the distinction between documented intent, historical context, and current runtime reality when both are available.

## Memory Bank structure and navigation

### Mandatory root Memory Bank files

Read all Markdown files directly under `memory-bank/` at the start of each session.

Recommended root files include:
- `memory-bank/projectbrief.md`
- `memory-bank/productContext.md`
- `memory-bank/activeContext.md`
- `memory-bank/systemPatterns.md`
- `memory-bank/techContext.md`
- `memory-bank/progress.md`

Optional additional root files may include:
- `memory-bank/domainGlossary.md`
- `memory-bank/serviceInventory.md`
- `memory-bank/integrationInventory.md`
- `memory-bank/apiInventory.md`
- `memory-bank/deploymentContext.md`
- `memory-bank/testingStrategy.md`
- `memory-bank/decisionLog.md`
- `memory-bank/customerKnowledgeIndex.md`
- `memory-bank/externalSystems.md`

If additional root Memory Bank files are added later, they are also part of the mandatory read.

### How to use the Memory Bank

- `projectbrief.md`
  - Foundation document that shapes all other files
  - Created at project start if it doesn't exist
  - Defines core requirements and goals
  - Source of truth for project scope

- `productContext.md`
  - Why this project exists
  - Problems it solves
  - How it should work
  - User experience goals
  - Business or operational outcomes

- `activeContext.md`
  - Current work focus
  - Recent changes
  - Next steps
  - Active decisions and considerations
  - Important patterns and preferences
  - Learnings and project insights

- `systemPatterns.md`
  - System architecture
  - Key technical decisions
  - Design patterns in use
  - Component relationships
  - Critical implementation paths

- `techContext.md`
  - Technologies used
  - Development setup
  - Technical constraints
  - Dependencies
  - Tool usage patterns

- `progress.md`
  - Confirmed findings, gaps, and evolution of understanding
  - What works
  - What's left to build
  - Current status
  - Known issues
  - Evolution of project decisions

- `domainGlossary.md` (optional)
  - Important domain language
  - Business entities and terminology
  - Ambiguous terms and canonical meanings

- `serviceInventory.md` (optional)
  - Verified service-, module-, or application-level summaries
  - Ownership boundaries
  - Runtime responsibilities

- `integrationInventory.md` (optional)
  - External systems
  - Message flows
  - Protocols, queues, topics, APIs, file interfaces, or webhooks

- `apiInventory.md` (optional)
  - Important APIs
  - Contracts
  - Versioning
  - Ownership and consumers

- `deploymentContext.md` (optional)
  - Environments
  - Delivery model
  - Infrastructure assumptions
  - Operational differences between local/dev/test/prod

- `testingStrategy.md` (optional)
  - Test layers
  - Test frameworks
  - How to validate changes safely

- `decisionLog.md` (optional)
  - Important architectural and product decisions
  - Trade-offs
  - Rejected alternatives

- `customerKnowledgeIndex.md` / `externalSystems.md` (optional)
  - Entry points for customer-, tenant-, partner-, or external-system-specific context

### Subdirectory guidance

Create additional files or folders under `memory-bank/` when they improve reuse and organization, for example:
- customer-specific notes
- tenant-specific behavior
- domain-specific deep dives
- feature documentation
- integration specifications
- API documentation
- testing strategies
- deployment procedures
- migration notes
- incident learnings

When such files exist, read them when the task touches their subject area or when a root Memory Bank file points to them.

## Memory Bank update obligations

- After significant implementation, update at least `memory-bank/activeContext.md` and `memory-bank/progress.md`.
- If you discover or correct architecture, integration, or platform behavior, update `memory-bank/systemPatterns.md` and/or `memory-bank/techContext.md`.
- If scope or product framing changes, update `memory-bank/projectbrief.md` and/or `memory-bank/productContext.md`.
- If you clarify domain, customer, tenant, integration, or external-system meaning, update the relevant Memory Bank file and any related index or navigation file.
- Do not finish significant work with newly learned context left only in chat.

Memory Bank updates are required when:
1. Discovering new project patterns
2. Correcting stale or inaccurate assumptions
3. After implementing significant changes
4. When the user explicitly requests `update memory bank`
5. When context needs clarification for future work

## Repository overview

### Project summary
- **Project name:** `BinStash`
- **Repository type:** `MONOREPO` (single solution with multiple projects)
- **Primary purpose:** Build artifact storage system using content-defined chunking, chunk deduplication, and a custom binary format to minimize redundant storage in CI/CD pipelines.
- **Primary business/domain area:** Developer tooling / CI/CD artifact management
- **Main entry points:** `BinStash.sln`, server entry: `BinStash.Server/Program.cs`, CLI entry: `BinStash.Cli/Program.cs`
- **License:** AGPLv3
- **Status:** Alpha

### Repository shape

**Top-level structure:**

| Directory/File | Purpose |
|---|---|
| `BinStash.Contracts/` | Shared DTOs, gRPC proto definitions, cross-project contracts |
| `BinStash.Core/` | Domain logic: chunking, ingestion, serialization, compression, storage abstractions |
| `BinStash.Infrastructure/` | EF Core DbContext, migrations, local pack-file storage implementation, email templates |
| `BinStash.Server/` | ASP.NET Core web host: REST endpoints, GraphQL (HotChocolate), gRPC ingest, auth, hosted services |
| `BinStash.Cli/` | CliFx-based CLI client communicating with the server via REST/gRPC |
| `BinStash.Core.Tests/` | xUnit tests for core domain logic |
| `BinStash.Serializers.Tests/` | Snapshot/regression tests for the binary release format serializer |
| `docs/` | Conceptual documentation (architecture, CLI reference, file format, performance, FAQ) |
| `compose.yaml` | Docker Compose for local development (PostgreSQL + Adminer) |
| `Jenkinsfile` | Jenkins CI pipeline (restore, build, test) |
| `Utils/` | Miscellaneous utility scripts/files |

**Primary deployable units:**
- `BinStash.Server` — containerized ASP.NET Core server (Dockerfile at `BinStash.Server/Dockerfile`)
- `BinStash.Cli` — self-contained CLI executable

**Important bounded contexts or modules:**
- Chunk storage (pack files + indexing)
- Release ingestion pipeline (chunking → deduplication → upload)
- Release download / delta download
- Multi-tenancy and RBAC
- GraphQL management API
- gRPC ingest streaming API

## Technology and architecture essentials

### Runtime style
- Modular monolith server (single deployable, internal layered structure)
- Request/response (REST + GraphQL) and streaming (gRPC) for ingest
- Background hosted services for chunk store probing and stats collection
- Database migrations run automatically at startup (`db.Database.Migrate()`)

### Primary technologies
- **Language:** C# / .NET 10
- **Server framework:** ASP.NET Core 10 (`Microsoft.NET.Sdk.Web`)
- **GraphQL:** HotChocolate 15 (queries, mutations, filtering, sorting, projections)
- **gRPC:** `Grpc.AspNetCore` (server) / `Grpc.Net.Client` (CLI client); proto at `BinStash.Contracts/Protos/ingest.proto`
- **REST/OpenAPI:** Minimal API endpoints + `Microsoft.AspNetCore.OpenApi` + Scalar reference UI (dev only)
- **Persistence:** PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL 10`; EF Core with code-first migrations
- **Identity/Auth:** ASP.NET Core Identity + JWT Bearer + Cookie + custom ApiKey scheme; "Smart" composite auth scheme
- **CLI:** CliFx 2 + Spectre.Console
- **Chunking:** FastCDC (content-defined chunking, `SlidingWindow` library)
- **Hashing:** BLAKE3 (chunk identity / content addressing), xxHash3 (pack file integrity), System.IO.Hashing
- **Compression:** Zstd via custom `ZstdNetNGX` fork; SharpZipLib on server
- **Email:** Brevo provider with Handlebars.Net HTML templates
- **Infrastructure/tooling:** Docker (Linux containers), Docker Compose, Jenkins CI

### Common project/module shape

Projects follow a conventional layered architecture:
- `BinStash.Contracts` — shared types, no logic
- `BinStash.Core` — domain logic and abstractions (no infrastructure dependencies)
- `BinStash.Infrastructure` — EF Core, storage implementations (depends on Core)
- `BinStash.Server` — web host, wires everything together (depends on Core + Infrastructure + Contracts)
- `BinStash.Cli` — CLI client (depends on Core + Contracts only)
- `*.Tests` — test projects depending only on the project under test (exception: `BinStash.Serializers.Tests` also depends on `BinStash.Contracts`)

### Core shared components

| Component | Role |
|---|---|
| `BinStash.Contracts` | Cross-project DTOs, gRPC proto, hashing types — no dependencies on other BinStash projects |
| `BinStash.Core/Chunking/FastCdcChunker` | Content-defined chunking engine (FastCDC algorithm) |
| `BinStash.Core/Serialization/ReleasePackageSerializer` | Custom binary format for `.rdef` release definition files |
| `BinStash.Infrastructure/Data/BinStashDbContext` | EF Core context — single database for all entities |
| `BinStash.Infrastructure/Storage/LocalFolderChunkStoreStorage` | Pack-file–based local chunk storage with `.pack`/`.idx` files |
| `BinStash.Server/GraphQL` | HotChocolate GraphQL management API (Query + Mutation types) |
| `BinStash.Server/Grpc/IngestGrpcService` | Streaming gRPC ingest endpoint (chunks + file definitions) |

### Major subsystems

- **Ingestion pipeline** — CLI discovers input (plain files or ZIP archives), chunks them via `FastCdcChunker` (FastCDC/BLAKE3), uploads missing chunks via gRPC streaming (`UploadChunks`), serializes file definitions to `.rdef` format and streams via gRPC (`UploadFileDefinitions`), then calls REST to register the release in the database.
- **Pack-file storage** — Chunks and file definitions are stored in BLAKE3-prefixed `.pack` files with accompanying `.idx` index files. Files rotate at 4 GiB.
- **Release format** — Custom binary `.rdef` format: transpose-compressed chunk-hash tables, varint-encoded sizes, Zstd-compressed sections, tokenized component/file name strings.
- **Multi-tenancy** — Configurable single-tenant or multi-tenant mode (`TenancySettings.Mode`). Tenant resolution happens via middleware. Authorization uses three permission scopes: Instance, Tenant, Repository.
- **Auth** — Composite "Smart" scheme auto-selects JWT Bearer / ApiKey / Cookie based on the `Authorization` header prefix. Separate "Setup" cookie scheme for first-run setup flow.
- **Background services** — `ChunkStoreProbeService` (liveness), `ChunkStoreStatsHostedService` (metrics snapshots), `SetupBootstrapper` (first-run init).
- **CLI local state** — `Microsoft.Data.Sqlite` stores local persistent state (auth tokens, server config); credentials protected with `Microsoft.AspNetCore.DataProtection`.

### Key patterns

- **Content-addressable storage:** chunks are identified by their BLAKE3 hash; deduplication is per-chunk-store.
- **Streaming gRPC ingest:** chunks and file definitions are streamed from CLI to server; server returns deduplication stats.
- **DB-backed configuration:** `DbConfigurationSource` loads instance settings from the database as the lowest-priority config source; all other sources (env, appsettings) override it.
- **EF Core with auto-migration:** `db.Database.Migrate()` is called in `Program.cs` startup — never manually run migrations in production without verifying startup behavior.
- **Resource-pool for recyclable memory:** `Microsoft.IO.RecyclableMemoryStream` is used in Core for buffer management.

## Architecture guardrails

- **Do not break the `BinStash.Core` / infrastructure boundary.** Core must not reference `BinStash.Infrastructure` or `BinStash.Server`. Infrastructure depends on Core, not the reverse.
- **Do not add infrastructure dependencies to `BinStash.Contracts`.** Contracts must remain a pure shared-types project (no EF Core, no ASP.NET Core, no infrastructure packages).
- **Deduplication is scoped to a single chunk store.** Do not assume cross-store deduplication.
- **Chunk identity is determined by BLAKE3 hash of chunk content.** Never change the hash function without a full migration plan for existing stores.
- **Pack files must not be manually edited or truncated.** The `.idx` file encodes absolute offsets; any pack corruption invalidates its index.
- **EF Core migrations are applied automatically at startup.** Do not add destructive migration operations without explicit operator approval.
- **JWT signing key must be set via configuration (not hardcoded).** The fallback `"dev-only-change-me"` key in `Program.cs` must never reach production.
- **The gRPC ingest service (`ingest.proto`) is a shared contract.** Changes to proto messages require updating both server and CLI builds. Maintain backward-compatible field numbering.
- **Tenancy mode is set in configuration.** Single-tenant vs. multi-tenant behavior diverges at middleware and bootstrapper level — verify both paths when changing tenant-related logic.

When changing a cross-cutting or integration-heavy flow, always inspect:
- inputs
- outputs
- ownership boundaries
- persistence timing
- idempotency / duplicate handling
- ordering assumptions
- configuration dependencies
- operational side effects

## Build and test entry points

### Build

```bash
# Full solution build (Release)
dotnet build BinStash.sln --configuration Release

# Build single project
dotnet build BinStash.Server/BinStash.Server.csproj --configuration Release
```

### Run tests

```bash
# All tests
dotnet test BinStash.sln --configuration Release

# Specific test project
dotnet test BinStash.Core.Tests/BinStash.Core.Tests.csproj
dotnet test BinStash.Serializers.Tests/BinStash.Serializers.Tests.csproj
```

### Local development (database)

```bash
# Start PostgreSQL + Adminer (port 6432 / 8880)
docker compose up -d
```

### EF Core migrations

```bash
# Add a migration (run from repo root)
dotnet ef migrations add <MigrationName> --project BinStash.Infrastructure --startup-project BinStash.Server

# Migrations are applied automatically on server startup (no manual update command needed in dev)
```

### Server (local)

```bash
dotnet run --project BinStash.Server/BinStash.Server.csproj
# OpenAPI / Scalar UI available at /scalar in Development mode
```

### Docker

```bash
# Build server image
docker build -f BinStash.Server/Dockerfile -t binstash-server .
```

## CI/CD

- **CI:** Jenkins pipeline (`Jenkinsfile` at repo root)
  - Stages: Checkout → SDK Info → Restore → Build → Test (optional via `RUN_TESTS` parameter)
  - Test results published as xUnit XML (`**/TestResults/*.xml`)
  - Uses the `dotnet-lts` Jenkins tool for SDK management
- **No automated deployment pipeline** is visible in this repository; deployment is manual or handled outside this repo.

## Configuration

Key configuration sections in `appsettings.json` / environment / secrets:

| Section | Purpose |
|---|---|
| `ConnectionStrings:BinStashDb` | PostgreSQL connection string (required; defaults to `<from-keyvault>` placeholder) |
| `Auth:Jwt:Issuer` | JWT token issuer |
| `Auth:Jwt:Key` | JWT signing key (must be set in production; never use the dev fallback) |
| `Domain` | `DomainSettings` — server domain configuration |
| `Tenancy` | `TenancySettings` — `Mode` (Single/Multi), `DefaultTenantId`, `DomainSuffix` |
| `Email` | `EmailSettings` — email provider configuration (Brevo) |

Additional instance-level settings are stored in the database and loaded via `DbConfigurationSource` (lowest priority, overridable by all other sources).

## Code style conventions

- Nullable reference types enabled (`<Nullable>enable</Nullable>`) across all projects.
- Implicit usings enabled across all projects.
- Target framework: `net10.0` for all projects.
- Copyright header required on all source files (AGPLv3 notice, author: Lukas Eßmann).
- Authorization policies follow the naming convention `Permission:<Scope>:<Level>` (e.g., `Permission:Repo:Write`).