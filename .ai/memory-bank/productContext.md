# Product Context

## Purpose
- Capture why this software exists, who it serves, and what outcomes matter.
- Preserve product and user framing that helps future engineering decisions.

## What does not belong here
- low-level implementation details
- transient roadmap or ticket status
- unsupported business assumptions

## Users And Stakeholders
- **CI/CD pipelines and the engineers who run them** — the primary producers, uploading build outputs as releases.
- **Artifact consumers / deployment systems** — download full releases, individual components, or deltas.
- **SaaS operators and tenants** — a multi-tenant deployment lets multiple teams/customers share one server with isolation, RBAC, and (via the commercial plugin) metered billing. A single-tenant mode also exists.
- **The project owner (Lukas Eßmann)** — sole author; the dual-license split (AGPL open-core vs. commercial billing plugin) is a deliberate commercial-protection boundary.

## Problems Solved
- **Redundant storage**: conventional artifact stores keep each upload whole; BinStash stores identical byte ranges once per chunk store via FastCDC + BLAKE3 dedup.
- **Slow / wasteful downloads**: delta download lets a client fetch only chunks it lacks.
- **Opaque artifact management**: GraphQL + REST expose repositories, releases, chunk stores, and metrics as queryable resources; a Vue frontend gives an admin/tenant UI.
- **Legacy migration**: the CLI's SVN-tags importer bulk-migrates tagged SVN releases with resumable, SQLite-backed state.

## Important Workflows
- **Server setup**: operator provisions PostgreSQL + a `LocalFolder` chunk store; a first-run setup gate (`SetupGateMiddleware` + `SetupBootstrapper`) blocks the API until initialized and logs a one-time setup code.
- **Release ingestion (CLI `release add`)**: discover inputs → FastCDC chunk + BLAKE3 hash → open an ingest session → gRPC stream only missing chunks and file definitions → REST finalize → `.rdef` registered against the release. Inputs may be plain directories or containers (ZIP/jar/nupkg/tar, etc.), the latter inspected and deterministically reconstructed rather than stored opaquely.
- **Retrieval (CLI `release download` / `install`)**: stream release content (full or delta) from pack storage.
- **Administration**: the Vue SPA drives tenant/repository/chunk-store/service-account management over Apollo GraphQL, plus REST for auth and a few admin paths; long operations (chunk-store rebuild, release upgrade) run as async background jobs.

## Product Constraints
- **The dual-license boundary must stay clean.** Commercial / metered-billing logic lives only in a plugin loaded by the server; `BinStash.Core` and the AGPL path must remain fully functional with the NoOp billing defaults (unlimited).
- **Binary-format backward compatibility.** The `.rdef` / `.pack` / `.idx` formats are versioned and load-bearing; readers must keep supporting older versions or migrate via `BinStash.StoreMigration`.
- **Deduplication scope is intentionally per-chunk-store**, not global.
- **Security / tenant isolation** (auth schemes, per-tenant scoping, quota/egress paths) is sensitive and must be verified before change.

## Success Criteria
- Measurable storage savings through chunk-level dedup; correct, lossless round-trips of ingested artifacts (guarded by serializer round-trip tests).
- Strict tenant isolation and correct permission enforcement.
- A working AOT CLI (native Zstd resolved into the expected runtime layout) and a frontend that bundles into the server's static root.

## Source Notes
- Derived from code (`Program.cs`, ingestion/serialization, `Endpoints/`, `Billing/`) and `README.md`. No external product/business documents were provided; treat business/pricing specifics as unverified.
