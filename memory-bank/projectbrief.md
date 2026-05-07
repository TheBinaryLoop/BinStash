# Project Brief

## Project name
BinStash

## Status
Alpha

## License
AGPLv3 — Copyright © 2025-2026 Lukas Eßmann

## Core goal
BinStash is a build artifact storage system for CI/CD pipelines. It dramatically reduces redundant storage by applying content-defined chunking (FastCDC), BLAKE3-based chunk deduplication, and a custom compressed binary release format (`.rdef`).

## Primary users
- Developers and CI/CD engineers storing and distributing build artifacts
- Systems that need partial/delta downloads of large release packages
- Teams that want immutable, content-addressed artifact storage

## Scope

### In scope
- Server: artifact ingestion, storage, retrieval, deduplication, multi-tenancy, RBAC
- CLI: repository management, release upload (chunk + stream), release install/download, analyze tools, SVN import
- Custom `.rdef` release definition binary format (V4 current)
- Pack-file–based local chunk storage (`.pack` + `.idx`)
- GraphQL management API (HotChocolate)
- gRPC streaming ingest API
- REST endpoints for identity, setup, repositories, releases, chunk stores, service accounts, storage classes, tenants
- Multi-tenant and single-tenant deployment modes

### Out of scope (at this time)
- S3/object-store chunk storage backend (referenced in docs but not yet implemented)
- Automated deployment pipeline (deployment is manual)
- Frontend UI (no web frontend exists; management is via GraphQL / REST)

## Repository structure
Monorepo — single `.slnx` (XML-based solution format) containing:
- `BinStash.Contracts` — shared DTOs, gRPC proto definitions
- `BinStash.Core` — domain logic (chunking, serialization, ingestion, compression, storage abstractions)
- `BinStash.Infrastructure` — EF Core DbContext, migrations (30 total), local pack-file storage, email templates
- `BinStash.Server` — ASP.NET Core host (REST, GraphQL, gRPC, auth, hosted services)
- `BinStash.Cli` — CliFx 3-based CLI executable (24 commands)
- `BinStash.Core.Tests` — unit + property-based tests for core domain logic
- `BinStash.Serializers.Tests` — snapshot/regression tests for the binary release format serializer (depends on Contracts + Core)
- `BinStash.Server.Tests` — server-side unit tests (ApiKey auth handler specs)
- `docs/` — conceptual documentation (architecture, CLI reference, file format, performance, FAQ) — partially stale
