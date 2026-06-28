# Project Brief

## Purpose
- Capture the stable high-level identity, scope, and boundaries of this repository.
- Explain why this Memory Bank exists and what future agents must know first.

## What does not belong here
- implementation history or progress logs
- detailed architecture patterns that belong in `systemPatterns.md`
- unresolved questions that belong in `knownGaps.md`

## Repository Identity
- Project/product name: **BinStash** — a content-defined-chunked, deduplicated, compressed store for build artifacts and release packages.
- Repository: local working tree `D:\Projects\BinStash`, git default branch `master`. Dual-licensed **AGPLv3 + commercial** (`LICENSE.txt` / `LICENSE-COMMERCIAL.md`); every C# file carries the `// Copyright (C) 2025-2026 Lukas Eßmann …` header.
- Solution: `BinStash.slnx` (XML `.slnx` format), target `net10.0`, `Nullable` + `ImplicitUsings` on per-project, platforms `AnyCPU`/`x64`.
- Primary entry points: **`BinStash.Server`** (ASP.NET Core API host — GraphQL + gRPC + REST), **`BinStash.Cli`** (AOT-published CliFx client), **`BinStash.Frontend`** (Vue 3 SPA, npm package name `mosaic-vue`, built into `BinStash.Server/wwwroot`).

## What This Project Is
- A storage system that ingests build outputs, splits files into content-defined chunks (FastCDC), addresses and deduplicates chunks by **BLAKE3**, records release metadata in a **custom versioned binary format** (`.rdef`, currently V6), and stores everything Zstd-compressed in on-disk pack files.
- A **multi-tenant SaaS server** with auth (JWT / cookie / API-key / service-account), per-tenant isolation, RBAC, GraphQL management API, gRPC streaming ingest, REST auth/ingest/setup/admin endpoints, and a billing boundary that keeps the AGPL open-core fully functional while allowing a commercial billing plugin.
- A CLI client for repositories/releases/chunk-stores plus an SVN-tags import path, and a Vue web frontend for administration.

## Main Outcomes
- Large reduction in redundant artifact storage via per-chunk-store deduplication.
- Partial / delta downloads of releases (fetch only missing chunks).
- Immutable, content-addressed, reproducible release packages.

## Scope Boundaries
- In scope: artifact ingestion / chunking / dedup / storage / retrieval; the `.rdef`/`.pack`/`.idx` binary formats; PostgreSQL-backed metadata; multi-tenancy + auth + RBAC; GraphQL/gRPC/REST APIs; the Vue frontend; CLI (incl. SVN import); local pack-file storage backend; the billing interface boundary (NoOp open-core + optional commercial plugin).
- Out of scope / **not present despite older docs/READMEs implying otherwise**: S3 / object-store chunk backend (only `LocalFolder` exists), automated deployment pipeline, the `docs/` directory (README links are aspirational), and the `Utils` tooling projects `RepackFileDefs` / `ChunkStoreExplorer` / `RdefAnalyzer` (do not exist on disk).

## Primary Source Material
- Code/configuration inspected: `BinStash.slnx` + all nine `.csproj`; `BinStash.Core/Serialization` + `Compression`; `BinStash.Server` (`Program.cs`, `Auth/`, `GraphQL/`, `Grpc/`, `Endpoints/`, `HostedServices/`, `Billing/`, `Configuration/`); `BinStash.Infrastructure` (`Storage/`, `Data/` + `Migrations/`); `BinStash.Cli`; `BinStash.Frontend` (`package.json`, `vite.config.js`, `src/`).
- Repository documentation inspected: `README.md` (partially stale — predates SaaS/auth/frontend), root `CLAUDE.md`, `.ai/AGENTS.md`, and the recovered prior `memory-bank/*` (deleted from the working tree; treated as background, not truth).
- User-provided sources inspected end-to-end: none provided. Build/tests were **not executed** this pass (see `knownGaps.md`).
