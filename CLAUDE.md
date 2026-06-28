@.ai/AGENTS.md

# CLAUDE.md

Claude Code guidance for the **BinStash** repository — a content-defined-chunked, deduplicated,
compressed store for build artifacts and release packages (.NET 10 / C#). The shared, cross-project
workflow lives in `.ai/AGENTS.md` (a git submodule) and must be followed in full. The rules below are
BinStash-specific and take precedence for this repo where they conflict.

## Memory identity

Use these for **every** generic `save_memory` / `search_memory` / `list_memories` / `update_memory` /
`delete_memory` call (see `.ai/AGENTS.md` › *Vector Memory › Memory identity*):

- `entityType`: `project`
- `entity`: `binstash`

Facts about the user themselves go to the about-the-user profile (`remember_about_user` /
`recall_about_user`), not a project memory.

## Repository overview

BinStash stores build artifacts efficiently with FastCDC content-defined chunking, BLAKE3-addressed
deduplication, a custom delta-encoded/varint-packed/transpose-compressed binary metadata format, and
transparent Zstd compression. It ships as a multi-tenant SaaS server, a CLI client, and a web
frontend. Dual-licensed **AGPLv3 + commercial** (see `LICENSE.txt` / `LICENSE-COMMERCIAL.md`).

The solution is `BinStash.slnx` (at the repo root; target `net10.0`, `Nullable` + `ImplicitUsings`
enabled everywhere; platforms `AnyCPU` / `x64`). The project folders live under `src/` (the six app
projects), `tests/` (the three test projects), and `tooling/` (`BinStash.StoreMigration`). Projects:

- **`BinStash.Contracts`** — DTOs and wire contracts shared by client and server (`Auth`,
  `ChunkStore`, `Delta`, `Hashing`, `Ingest`, `Release`, `Repo`, `ServiceAccount`, `StorageClass`,
  `Tenant`) plus the gRPC `Protos/ingest.proto`. No project dependencies.
- **`BinStash.Core`** — domain + algorithms: `Chunking` (FastCDC), `Compression` (Zstd via
  `ZstdNetNGX`), `Serialization` (the custom `.pack`/`.idx`/`.rdef` binary format), `Ingestion`,
  `Storage`, `Entities`, `Auth`, and the `Billing` boundary (interfaces + `NoOp` defaults). Uses
  `Blake3`, ASP.NET Identity EF Core. References `Contracts`.
- **`BinStash.Infrastructure`** — persistence + IO: `Data` (`BinStashDbContext` + EF Core
  migrations, PostgreSQL via `Npgsql`), the `LocalFolder` `Storage` backend (the only one
  implemented), Handlebars (`.hbs`) email `Templates` (embedded). References `Core`. Declares
  `InternalsVisibleTo` for `StoreMigration`, `RepackFileDefs`, and `ChunkStoreExplorer` — but only
  `StoreMigration` exists on disk; the latter two are dangling (no such projects).
- **`BinStash.Server`** (`Microsoft.NET.Sdk.Web`) — the API host: HotChocolate **GraphQL** (15.x),
  **gRPC** ingest endpoint, JWT + cookie + API-key auth, multi-tenancy, the billing plugin loader,
  health checks, Scalar/OpenAPI. Runs as a systemd/Windows service; `Dockerfile` targets Linux.
  References `Contracts`, `Core`, `Infrastructure`.
- **`BinStash.Cli`** (AOT-published) — `CliFx` + `Spectre.Console` client. Commands: `auth`,
  `chunk-store`, `repo`, `release`, `analyze`, `test`, and `svn import-tags`. gRPC client for ingest.
- **`BinStash.StoreMigration`** — console tool over `Infrastructure` internals for store/schema
  migration. Its `.csproj` lives at `tooling/BinStash.StoreMigration` but is **not referenced by
  `BinStash.slnx`** (the solution's `/Tooling/` folder is empty), so it does not build with the
  solution.
- **`BinStash.Frontend`** — Vue 3 + Vite + Pinia + Tailwind v4 + TypeScript SPA, talking to the
  server over a hybrid of Apollo **GraphQL** (HTTP + `graphql-ws` for subscriptions) and **REST**
  (`/api`, `/health`). `pnpm build` emits the bundle into `src/BinStash.Server/wwwroot`. Package manager
  is **pnpm** (via corepack); package name `mosaic-vue`.
- Tests: `BinStash.Core.Tests`, `BinStash.Serializers.Tests`, `BinStash.Server.Tests` — xUnit +
  FluentAssertions; Core adds FsCheck + Verify, Serializers adds Verify, Server uses `Mvc.Testing` +
  EF Core InMemory. (There is no real `BinStash.Infrastructure.Tests` project — only stale
  `bin/`/`obj/` remnants on disk.)
- `README.md` is the top-level architecture/quick-start reference. (The `docs/` paths it links are
  aspirational — that directory is not present; do not treat it as authoritative.)

## Build, test, run

- **Build / test the solution** (run from repo root; `dotnet`, not bash scripts):
  ```
  dotnet build BinStash.slnx -c Release
  dotnet test  BinStash.slnx -c Release
  ```
  Or target a single test project, e.g.
  `dotnet test tests/BinStash.Server.Tests/BinStash.Server.Tests.csproj`.
- **Frontend** (in `src/BinStash.Frontend/`):
  ```
  corepack enable && corepack prepare pnpm@latest --activate
  pnpm install --frozen-lockfile
  pnpm dev      # local dev (vite, mkcert https)
  pnpm build    # production bundle
  ```
- **EF Core migrations** live in `src/BinStash.Infrastructure/Data/Migrations`; the server is the
  startup/design-time host (it carries `Microsoft.EntityFrameworkCore.Design`). Add migrations with
  `dotnet ef migrations add <Name> --project src/BinStash.Infrastructure --startup-project src/BinStash.Server`.
- **CLI AOT publish** runs the `FixZstdNativeLayout` target to relocate `libzstd` into the
  `runtimes/<rid>/native/` layout `ZstdNetNGX`'s resolver expects — don't break that target.
- **CI**: `Jenkinsfile` builds the frontend (pnpm), then `dotnet restore`/`build`/`test` (xUnit
  logger) on `dotnet-lts`. Platform here is win32; the server container is Linux.

## Project-specific guardrails

- **Keep the AGPL license header** at the top of every C# source file (`// Copyright (C) 2025-2026
  Lukas Eßmann …`). New source files must carry it.
- **The billing boundary enforces the dual-license split.** `BinStash.Core/Billing` defines the
  interfaces (`IBillingProvider`, `IBillingLimits`, `IUsageMeteringService`,
  `IBillingPluginRegistrar`) with `NoOp` implementations so the open-source/AGPL core is fully
  functional standalone; the commercial billing logic is a plugin loaded by
  `BinStash.Server/Billing/BillingPluginLoader`. Do **not** bake commercial/metered billing logic
  into `Core` or the AGPL path — extend via the plugin boundary, and keep the `NoOp` path working.
- **The custom binary format is load-bearing and versioned.** Changes to `Core/Serialization` and the
  on-disk `.pack`/`.idx`/`.rdef` structures (BLAKE3-prefixed packs, `BSPK` magic, xxHash3 checksums,
  4 GiB rotation) must preserve backward compatibility or go through `BinStash.StoreMigration`. Treat
  these carefully.
- **Don't hand-edit existing EF migrations** under `Data/Migrations`; generate new ones. The schema
  is PostgreSQL-specific (`Npgsql`).
- **Auth, tenancy, and quota enforcement are security-sensitive.** JWT/cookie/API-key/service-account
  auth, per-tenant isolation, storage scoping, and the 402 quota/egress metering paths must be
  verified in code before changing — don't loosen tenant or auth checks.
- **`ZstdNetNGX` is a custom fork** (`TheBinaryLoop/ZstdNetNGX`) — match existing usage; don't swap in
  upstream ZstdNet.
- **Don't commit build output** (`bin/`, `obj/`, `node_modules/`, the frontend bundle in
  `src/BinStash.Server/wwwroot` — all gitignored).
- Code style follows `BinStash.sln.DotSettings` (ReSharper); match the surrounding code.
