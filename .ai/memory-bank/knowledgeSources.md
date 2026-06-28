# Knowledge Sources

## Purpose
- Record where durable knowledge about this repository comes from, and how reliable each source is.
- Point future agents at the project's external source-of-record (the Plane tracker) and flag which in-repo docs are stale.

## What does not belong here
- copies of source content (link/reference instead)
- transient ticket status or a backlog dump
- agent/bootstrap process narration

## Scope And Confidence
- This Memory Bank was built **code-first**, verifying current behavior across all six first-class projects via targeted reads (serialization, server, billing/core, infrastructure, CLI, frontend/tooling). Builds and tests were **not executed**. Where a claim is inferred rather than runtime-verified, it is flagged in `knownGaps.md`.

## Primary Sources
- **The code and configuration** — the authoritative truth for behavior (`Program.cs`, `Core/Serialization`, `Endpoints/`, `Billing/`, `Storage/`, `Data/Migrations`, `.csproj` files, `vite.config.js`, `package.json`, `Jenkinsfile`, `Dockerfile`).
- **Plane** — the project-management source-of-record (the user confirmed PM lives in Plane, queried via the `plane` MCP, tools `mcp__plane__*`):
  - Workspace `47955bd6-2f3a-4af2-b4d1-370441bc8158`; project **BinStash** id `761b0dc7-c018-4b1c-9e2a-5f0eaff2a2ae`, identifier **`BINST`**.
  - ~93 work items (snapshot 2026-06-27: **25 Done, 67 Backlog, 1 Todo**). States: `Backlog` (default) / `Todo` / `In Progress` / `Done` / `Cancelled`.
  - Title conventions: feature/epic tickets are plain `BINST-N`; code-review findings are prefixed in their titles — `QC-` (quality), `PERF-`, `ERR-` (error handling), `DES-` (design/dead code), `DEAD-`, `CFG-` (config).
  - The completed `BINST-91…100` chain (polymorphic backend settings, ChunkerOptions, the async release-upgrade pipeline, the LSM segmented index, chunk-store rebuild as a background job) matches the architecture verified in code.
  - The project **overview page still shows Plane's default demo template text** — ignore it; the work items are the real content.
  - To use: `retrieve_work_item_by_identifier("BINST-93")` for one ticket; `list_work_items(project_id, …)` with PQL/`order_by`; `childOf("BINST-…")` for epic children. Treat ticket titles as intent, not current truth (e.g. `BINST-97` says "SignalR" but the code uses HotChocolate subscriptions).

## Secondary / Background Sources (treat as partially stale)
- **Recovered prior `memory-bank/*`** (deleted from the working tree; read from git for background): predates the SaaS/auth/frontend work, claimed `.rdef` V4 (now V6) and an incomplete CliFx migration (now complete). Useful for history; **not** current truth.
- **`README.md`**: predates SaaS/auth/frontend; links an aspirational `docs/` directory that does not exist.
- **`CLAUDE.md` + `.ai/AGENTS.md`**: authoritative for agent workflow, guardrails, and the project's memory identity (`entityType: project`, `entity: binstash`).
