# Tech Context

## Purpose
- Capture durable technical, tooling, runtime, and infrastructure context for this repository.
- Record commands and conventions future agents need before editing or verifying changes.

## What does not belong here
- detailed feature behavior
- temporary environment incidents
- unresolved knowledge gaps that belong in `knownGaps.md`

## Technology Stack
- Languages/frameworks: **C# on `net10.0`** (`Nullable` + `ImplicitUsings` enabled per `.csproj`). Server is `Microsoft.NET.Sdk.Web` (ASP.NET Core); CLI is a console exe; frontend is **Vue 3.5 + Vite 8 + Pinia 3 + vue-router 5 + TypeScript 5.9 + Tailwind v4**, talking to the server via **Apollo Client 3.14** (+ `graphql-ws` 6) and `fetch`.
- Server libraries: **HotChocolate 15.1.14** (GraphQL), **Grpc.AspNetCore 2.76**, **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1**, JwtBearer / OpenAPI / **Scalar** (dev), `AspNetCore.HealthChecks.NpgSql`, `Handlebars.Net`, `SharpZipLib`, hosting for systemd + Windows services.
- CLI libraries: **CliFx 3.0.0**, **Spectre.Console 0.55.0**, `Grpc.Net.Client`, `Microsoft.Data.Sqlite` (SVN-import state), `System.Security.Cryptography.ProtectedData`, Polly.
- Cross-cutting: **`ZstdNetNGX` 1.0.0** (custom ZstdNet fork — do not swap for upstream), **`Blake3`** (content hashing), `System.IO.Hashing` (xxHash3 pack checksums), `Microsoft.IO.RecyclableMemoryStream`. Chunking uses a **custom FastCDC implementation** (`FastCdcChunker`, own seeded gear table); the `SlidingWindow` package is also referenced.
- Package manager (frontend): **pnpm via corepack** (`pnpm-lock.yaml`, `pnpm-workspace.yaml`).

## Repository Layout
- `BinStash.Contracts` / `BinStash.Core` / `BinStash.Infrastructure` / `BinStash.Server` / `BinStash.Cli` — the five first-class projects (see `serviceInventory.md`).
- `BinStash.Frontend/` — Vue SPA (`src/app`, `src/api`, `src/shared`, `src/features`, `src/pages`, `src/components`); builds to `../BinStash.Server/wwwroot`.
- `BinStash.Core.Tests` / `BinStash.Serializers.Tests` / `BinStash.Server.Tests` — the three test projects.
- `BinStash.StoreMigration/` — one-shot store/schema migration console tool (`.csproj` on disk but **not referenced by `BinStash.slnx`**).
- `.ai/` — shared agent submodule (`AGENTS.md`) + this `memory-bank/`.

## Build, Test, And Run Commands
- Restore/build/test the solution (from repo root; use `dotnet`, not shell scripts):
  - `dotnet build BinStash.slnx -c Release`
  - `dotnet test BinStash.slnx -c Release` (or a single project, e.g. `dotnet test BinStash.Server.Tests/BinStash.Server.Tests.csproj`).
- Frontend (in `BinStash.Frontend/`): `corepack enable` → `pnpm install --frozen-lockfile` → `pnpm dev` (Vite, mkcert HTTPS, port 8080, proxies `/api` `/health` `/graphql` to `https://localhost:7117`) → `pnpm build` (outputs to the server's `wwwroot`).
- EF Core migrations: `dotnet ef migrations add <Name> --project BinStash.Infrastructure --startup-project BinStash.Server` (the Server is the design-time host). Never hand-edit existing migrations; the schema is PostgreSQL-specific.
- CLI AOT publish runs the `FixZstdNativeLayout` target to relocate `libzstd.*` into `runtimes/<rid>/native/` for `ZstdNetNGX`'s resolver — do not break it.

## Configuration And Environments
- Config sources (highest→lowest): environment variables, `appsettings.{Environment}.json`, `appsettings.json`, user secrets (dev), then **`DbConfigurationSource`** (the `InstanceSettings` table) inserted at index 0. Strongly-typed options: `Domain`, `Tenancy`, `Email`, `Auth`/`Auth:Jwt`, `Storage`, `VersionGate`, `RequestMetrics` (Jwt + Storage validated at startup).
- Billing plugin path comes only from env var **`BINSTASH_BILLING_PLUGIN_PATH`**; absent → NoOp billing.
- Platform here is win32 (dev); the server container targets Linux. The server registers both `AddSystemd()` and `AddWindowsService()`.

## Data And External Dependencies
- **PostgreSQL** via Npgsql; migrations auto-apply at startup. There is **no `compose.yaml`** in the repo — a developer must provide their own PostgreSQL (older docs referencing `docker compose up` / Adminer no longer apply).
- **Email**: `BrevoEmailProvider` (HTTP `POST https://api.brevo.com/v3/smtp/email`, `api-key` header) lives in `BinStash.Server/Email/`; Handlebars `.hbs` templates are embedded in `BinStash.Infrastructure`.
- On-disk chunk store (`LocalFolder` only) — see `dataModelNotes.md`.

## Tooling Notes
- **No central build/style config**: no `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `nuget.config`, or `.editorconfig`. Package versions are per-`.csproj` (and drift — e.g. `coverlet.collector` 8.0.1 vs 10.0.0 across test projects). Code style is governed by the ReSharper **`BinStash.sln.DotSettings`**, which also defines the AGPL file-header template (`Copyright (C) 2025-${CurrentDate.Year} Lukas Eßmann`). Match surrounding code.
- **CI**: `Jenkinsfile` (Windows `bat`; tools `dotnet-lts` + `node-lts`): Checkout → Frontend (pnpm install+build) → Restore → Build (`--no-restore`) → Test (xUnit logger, publishes `**/TestResults/*.xml`). `RUN_TESTS` param.
- **Container**: `BinStash.Server/Dockerfile` is multi-stage (`aspnet:10.0` / `sdk:10.0`), Linux, **server-only** (does not build the frontend in-image), exposes 8080/8081.
- `.github/dependabot.yml` runs weekly NuGet updates from `/`.

## Source Notes
- Versions and commands verified from the `.csproj` files, `Program.cs`, `Jenkinsfile`, `Dockerfile`, `vite.config.js`, and `package.json`. Build/tests were not executed this pass.
