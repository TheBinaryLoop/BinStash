# Tech Context

## Runtime and language

- **Language:** C# with nullable reference types and implicit usings enabled across all projects.
- **Target framework:** `net10.0` (all projects, including `Utils/RdefAnalyzer`).
- **Solution format:** `.slnx` (XML-based solution format, not classic `.sln`).
- **Server SDK:** `Microsoft.NET.Sdk.Web`.
- **CLI SDK:** `Microsoft.NET.Sdk` (console exe, `OutputType=Exe`).

## Key package versions (verified from `.csproj` files, 2026-04-13)

| Package | Version | Used in |
|---|---|---|
| `HotChocolate.AspNetCore` | 15.1.14 | Server |
| `HotChocolate.Data.EntityFramework` | 15.1.14 | Server |
| `Grpc.AspNetCore` | 2.76.0 | Server |
| `Grpc.Net.Client` | 2.76.0 | CLI |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.1 | Server, Infrastructure |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.5 | Server |
| `Microsoft.AspNetCore.OpenApi` | 10.0.5 | Server |
| `Scalar.AspNetCore` | 2.13.22 | Server (dev only) |
| `Blake3` | 2.2.1 | Server, Core |
| `SlidingWindow` | 1.0.7.1 | Core (FastCDC) |
| `ZstdNetNGX` | 1.0.0 | Core, Infrastructure, Server, CLI |
| `Microsoft.IO.RecyclableMemoryStream` | 3.0.1 | Core |
| `System.IO.Hashing` | 10.0.5 | Core, Infrastructure, CLI |
| `Handlebars.Net` | 2.1.6 | Infrastructure (email templates) |
| `SharpZipLib` | 1.4.2 | Server |
| `CliFx` | 3.0.0 | CLI |
| `Spectre.Console` | 0.55.0 | CLI |
| `Microsoft.Data.Sqlite` | 10.0.5 | CLI (local state) |
| `Microsoft.AspNetCore.DataProtection` | 10.0.5 | CLI |
| `Microsoft.Extensions.Hosting.Systemd` | 10.0.5 | Server |
| `Microsoft.Extensions.Hosting.WindowsServices` | 10.0.5 | Server |
| `AspNetCore.HealthChecks.NpgSql` | 9.0.0 | Server |
| `Microsoft.EntityFrameworkCore.InMemory` | 10.0.5 | Server.Tests |
| `xunit` | 2.9.3 | Tests |
| `FluentAssertions` | 8.9.0 | Tests |
| `FsCheck.Xunit` | 3.3.2 | Tests (property-based) |
| `Verify.Xunit` | 31.12.5 | Tests (snapshot) |
| `Microsoft.NET.Test.Sdk` | 18.4.0 | Tests |
| `coverlet.collector` | 8.0.1 | Tests (coverage) |
| `XunitXml.TestLogger` | 8.0.0 | Tests (CI output) |

### CliFx 3.0.0 upgrade note

CliFx was upgraded from 2.x to 3.0.0 (major version). Key API changes:
- `UseTypeActivator` replaced by `UseTypeInstantiator` (DI registration in `Program.cs`)
- `CliFx.Attributes` namespace renamed to `CliFx.Binding` (affects all command files)
- `ScalarInputConverter<T>` / `SequenceInputConverter<T>` renamed (affects `KeyValuePairConverter.cs`)
- `CommandLineApplicationBuilder` is the new entry point (replaces `CliApplicationBuilder`)

## Development prerequisites

- .NET 10 SDK
- Docker (for local PostgreSQL via `compose.yaml`)
- PostgreSQL (port 5432 locally via Docker Compose; Adminer at port 8880)
- User secrets configured for `BinStash.Server` (`UserSecretsId: 71fd78af-9cb7-4e7e-9539-9b6f11dd2d17`)

## Local dev database

```bash
docker compose up -d
# PostgreSQL: localhost:5432, user: binstash, password: Buggy-Emphasize8-Smell
# Adminer: http://localhost:8880
```

Connection string for local dev (set via user secrets or `appsettings.Development.json`):
```
Host=localhost;Port=5432;Database=binstash;Username=binstash;Password=Buggy-Emphasize8-Smell
```

**Note:** `appsettings.Development.json` uses `Email2` section (not `Email`), effectively disabling email in dev.

## Docker

- Server Dockerfile: `BinStash.Server/Dockerfile`
- Multi-stage build: `aspnet:10.0` base / `sdk:10.0` build
- Exposes ports 8080 (HTTP) and 8081 (HTTPS)
- Target OS: Linux
- `.dockerignore` excludes `bin/`, `obj/`, `.git/`, etc.

## CI

- Jenkins pipeline (`Jenkinsfile`)
- Tool: `dotnet-lts` (Jenkins .NET SDK tool)
- Stages: Checkout → SDK Info → Restore → Build → Test
- Test results: xUnit XML at `**/TestResults/*.xml`
- `RUN_TESTS` boolean parameter (default: true)

## Static analysis and quality tools

- **Dependabot:** Configured at `.github/dependabot.yml` for weekly NuGet updates.
- **Qodana:** Configured at `qodana.yaml` for .NET static analysis (no CI integration yet).
- **dotnet-tools:** `.config/dotnet-tools.json` includes:
  - `dotnet-reportgenerator-globaltool` v5.4.17 (coverage reports)
  - `dotnet-stryker` v4.8.1 (mutation testing)

## Testing

- Framework: xUnit 2
- Assertions: FluentAssertions
- Property-based: FsCheck.Xunit
- Snapshot/regression: Verify.Xunit (used in `BinStash.Serializers.Tests`)
- Coverage: `coverlet.collector`
- Test projects:
  - `BinStash.Core.Tests` — unit + property-based tests for core domain logic
  - `BinStash.Serializers.Tests` — round-trip tests for `.rdef` format (depends on Contracts + Core)
  - `BinStash.Server.Tests` — server-side unit tests (ApiKey auth handler, uses EF Core InMemory)

## CLI local state

- The CLI uses `Microsoft.Data.Sqlite` for local persistent state (e.g., auth tokens, server config).
- `Microsoft.AspNetCore.DataProtection` is used for protecting stored credentials.

## Email

- Provider: Brevo (HTTP API via `BrevoEmailProvider` + `HttpClient`)
- Templates: Handlebars.Net `.hbs` files embedded in `BinStash.Infrastructure` assembly under `Templates/`

## Compression

- `ZstdNetNGX` — custom fork of ZstdNet used in Core, Infrastructure, Server, and CLI
- `SharpZipLib` — available on Server for additional zip operations

## Database migrations

- 31 migrations in `BinStash.Infrastructure/Data/Migrations/`
- Most recent: `2026-04-14 PolymorphicChunkStoreBackendSettings`
- Applied automatically at startup via `db.Database.Migrate()`
