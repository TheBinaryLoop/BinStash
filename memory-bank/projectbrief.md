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
- CLI: repository management, release upload (chunk + stream), release install/download, analyze tools
- Custom `.rdef` release definition binary format
- Pack-file–based local chunk storage (`.pack` + `.idx`)
- GraphQL management API (HotChocolate)
- gRPC streaming ingest API
- REST endpoints for identity, setup, repositories, releases, chunk stores
- Multi-tenant and single-tenant deployment modes

### Out of scope (at this time)
- S3/object-store chunk storage backend (referenced in docs but not yet implemented)
- Automated deployment pipeline (deployment is manual)
- Frontend UI (no web frontend exists; management is via GraphQL / REST)

## Repository structure
Monorepo — single `.sln` containing:
- `BinStash.Contracts` — shared DTOs, proto
- `BinStash.Core` — domain logic
- `BinStash.Infrastructure` — EF Core, storage
- `BinStash.Server` — ASP.NET Core host
- `BinStash.Cli` — CLI executable
- `BinStash.Core.Tests` — unit + property-based tests for core domain logic
- `BinStash.Serializers.Tests` — snapshot/regression tests for the binary release format serializer (depends on Contracts + Core)
