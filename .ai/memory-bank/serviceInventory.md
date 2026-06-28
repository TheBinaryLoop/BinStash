# Service Inventory

## Purpose
- Summarize the build/deploy units of this repository so future work can target the right project.
- State scope/confidence where coverage is representative.

## What does not belong here
- exhaustive code walkthroughs
- implementation history or ticket notes
- per-API contract detail (see `interfaceInventory.md`)

## Scope And Confidence
- Covers every project in `BinStash.slnx` plus the on-disk `StoreMigration` tool. Verified from the `.csproj` files and `BinStash.slnx`. Confidence: high for existence/role; build not executed.

## Inventory

### `BinStash.Contracts`
- Purpose: DTOs/wire contracts shared by client and server + the gRPC `Protos/ingest.proto`.
- Entry points: contract namespaces (`Auth`, `ChunkStore`, `Delta`, `Hashing`, `Ingest`, `Release`, `Repo`, `ServiceAccount`, `StorageClass`, `Tenant`).
- Depends on: nothing internal. Consumed by Server, Cli, Core.

### `BinStash.Core`
- Purpose: domain + algorithms — `Chunking` (FastCDC), `Compression` (Zstd via `ZstdNetNGX`, transpose hash compression), `Serialization` (`.rdef`/`.pack`/`FileDefinitionRecord`), `Ingestion`, `Storage` (abstractions), `Entities`, `Auth` (enums/interfaces), `Billing` (interfaces + NoOp).
- Depends on: `Contracts`. **Must never reference `Infrastructure` or `Server`.**

### `BinStash.Infrastructure`
- Purpose: persistence + IO — `Data` (`BinStashDbContext` + EF migrations, PostgreSQL), `Storage` (pack files + LSM index, `LocalFolder` backend), Handlebars email template rendering.
- Depends on: `Core`. Declares `InternalsVisibleTo` for `StoreMigration` / `RepackFileDefs` / `ChunkStoreExplorer` (latter two don't exist).

### `BinStash.Server` (deployable — API host)
- Purpose: ASP.NET Core host — "Smart" composite auth, GraphQL (HotChocolate), gRPC ingest, REST endpoints, multi-tenancy, hosted/background services, billing plugin loader, config layering, health checks, serves the SPA from `wwwroot`.
- Entry points: `Program.cs`; `GraphQL/`, `Grpc/`, `Endpoints/`, `Auth/`, `HostedServices/`, `Billing/`, `Email/`.
- Depends on: `Contracts`, `Core`, `Infrastructure`. Runs as systemd / Windows service; `Dockerfile` targets Linux.

### `BinStash.Cli` (deployable — client, AOT)
- Purpose: CliFx + Spectre.Console client. Commands: `auth`, `chunk-store`, `repo`, `release`, `analyze`, `test`, `svn import-tags`. REST + gRPC clients; release-add orchestration; DPAPI/AES credential store; SQLite-backed SVN import.
- Depends on: `Contracts`, `Core` only. `PublishAot=true` (with the `FixZstdNativeLayout` target).

### `BinStash.Frontend` (deployable — SPA, npm `mosaic-vue`)
- Purpose: Vue 3 + Vite + Pinia + Tailwind v4 + TypeScript admin/tenant UI. Apollo split-link (GraphQL HTTP + `graphql-ws`) plus `fetch` to `/api`.
- Build: pnpm; `pnpm build` outputs to `../BinStash.Server/wwwroot`.

### `BinStash.StoreMigration` (tool, not in solution)
- Purpose: one-shot console tool over Infrastructure internals for store/schema migration.
- Note: `.csproj` exists on disk but is **not referenced by `BinStash.slnx`**.

### Test projects
- `BinStash.Core.Tests` — xUnit + FluentAssertions + **FsCheck** (property) + **Verify** (snapshot); references Core (+ Server build-only for billing-boundary tests). ~229 tests.
- `BinStash.Serializers.Tests` — **Verify** round-trip tests over embedded `.rdef` samples (the binary-format safety net). ~45 tests.
- `BinStash.Server.Tests` — **`Mvc.Testing`** + EF Core InMemory. ~19 tests.
- (`BinStash.Infrastructure.Tests` has leftover `bin/obj` but **no `.csproj`** — not a real project.)
