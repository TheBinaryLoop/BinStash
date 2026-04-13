# Tech Context

## Runtime and language

- **Language:** C# with nullable reference types and implicit usings enabled across all projects.
- **Target framework:** `net10.0` (all projects).
- **Server SDK:** `Microsoft.NET.Sdk.Web`.
- **CLI SDK:** `Microsoft.NET.Sdk` (console exe, `OutputType=Exe`).

## Key package versions (verified from `.csproj` files)

| Package | Version | Used in |
|---|---|---|
| `HotChocolate.AspNetCore` | 15.1.12 | Server |
| `HotChocolate.Data.EntityFramework` | 15.1.12 | Server |
| `Grpc.AspNetCore` | 2.76.0 | Server |
| `Grpc.Net.Client` | 2.76.0 | CLI |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.0 | Server, Infrastructure |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.0 | Server |
| `Microsoft.AspNetCore.OpenApi` | 10.0.4 | Server |
| `Scalar.AspNetCore` | 2.13.5 | Server (dev only) |
| `Blake3` | 2.2.0 | Server, Core |
| `SlidingWindow` | 1.0.7.1 | Core (FastCDC) |
| `ZstdNetNGX` | 1.0.0 | Core, Infrastructure, Server, CLI |
| `Microsoft.IO.RecyclableMemoryStream` | 3.0.1 | Core |
| `System.IO.Hashing` | 10.0.4 | Core, Infrastructure, CLI |
| `Handlebars.Net` | 2.1.6 | Infrastructure (email templates) |
| `SharpZipLib` | 1.4.2 | Server |
| `CliFx` | 2.3.6 | CLI |
| `Spectre.Console` | 0.54.0 | CLI |
| `Microsoft.Data.Sqlite` | 10.0.5 | CLI (local state) |
| `Microsoft.AspNetCore.DataProtection` | 10.0.0 | CLI |
| `Microsoft.Extensions.Hosting.Systemd` | 10.0.4 | Server |
| `Microsoft.Extensions.Hosting.WindowsServices` | 10.0.4 | Server |
| `AspNetCore.HealthChecks.NpgSql` | 9.0.0 | Server |
| `xunit` | 2.9.3 | Tests |
| `FluentAssertions` | 8.8.0 | Tests |
| `FsCheck.Xunit` | 3.3.2 | Tests (property-based) |
| `Verify.Xunit` | 31.12.5 | Tests (snapshot) |

## Development prerequisites

- .NET 10 SDK
- Docker (for local PostgreSQL via `compose.yaml`)
- PostgreSQL (port 6432 locally via Docker Compose; Adminer at port 8880)
- User secrets configured for `BinStash.Server` (`UserSecretsId: 71fd78af-9cb7-4e7e-9539-9b6f11dd2d17`)

## Local dev database

```bash
docker compose up -d
# PostgreSQL: localhost:6432, user: binstash, password: Buggy-Emphasize8-Smell
# Adminer: http://localhost:8880
```

Connection string for local dev (set via user secrets or `appsettings.Development.json`):
```
Host=localhost;Port=6432;Database=binstash;Username=binstash;Password=Buggy-Emphasize8-Smell
```

## Docker

- Server Dockerfile: `BinStash.Server/Dockerfile`
- Multi-stage build: `aspnet:9.0` base / `sdk:9.0` build (NOTE: should be updated to `10.0` — see activeContext.md)
- Exposes ports 8080 (HTTP) and 8081 (HTTPS)
- Target OS: Linux

## CI

- Jenkins pipeline (`Jenkinsfile`)
- Tool: `dotnet-lts` (Jenkins .NET SDK tool)
- Stages: Checkout → SDK Info → Restore → Build → Test
- Test results: xUnit XML at `**/TestResults/*.xml`
- `RUN_TESTS` boolean parameter (default: true)

## Testing

- Framework: xUnit 2
- Assertions: FluentAssertions
- Property-based: FsCheck.Xunit
- Snapshot/regression: Verify.Xunit (used in `BinStash.Serializers.Tests`)
- Mutation testing config present: `BinStash.Core.Tests/stryker-config.json`
- Coverage: `coverlet.collector`

## CLI local state

- The CLI uses `Microsoft.Data.Sqlite` for local persistent state (e.g., auth tokens, server config).
- `Microsoft.AspNetCore.DataProtection` is used for protecting stored credentials.

## Email

- Provider: Brevo (HTTP API via `BrevoEmailProvider` + `HttpClient`)
- Templates: Handlebars.Net `.hbs` files embedded in `BinStash.Infrastructure` assembly under `Templates/`

## Compression

- `ZstdNetNGX` — custom fork of ZstdNet used in Core, Infrastructure, Server, and CLI
- `SharpZipLib` — available on Server for additional zip operations
