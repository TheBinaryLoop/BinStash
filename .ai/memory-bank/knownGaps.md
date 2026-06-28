# Known Gaps

## Purpose
- Track important unresolved repository-knowledge gaps and suggested investigation targets.
- Make uncertainty explicit so future agents do not treat assumptions as facts.

## What does not belong here
- feature backlogs (the Plane tracker is the backlog — see `knowledgeSources.md`)
- implementation diaries or progress logs
- stable verified facts that belong in other Memory Bank files

## Current Blind Spots
- **Build/tests were not executed** while populating this Memory Bank. Test counts (~229 `Core.Tests`, ~45 `Serializers.Tests`, ~19 `Server.Tests`) are raw `[Fact]/[Theory]/[Property]` greps, not run results, and the CLI "compiles cleanly / AOT-publishes" conclusion is from static inspection (CliFx 3.0 APIs resolve, commit `3476bb2`, built DLL present) — not a build.
- **GraphQL subscription path looks incomplete.** Rebuild/upgrade services broadcast progress via `ITopicEventSender` on `BackgroundJobProgress_{jobId}` and the frontend has a job-progress composable, but **no `Subscription` root type / `[Subscribe]` resolver is registered server-side**, so live progress may not actually reach clients. (Plane `BINST-97` is titled "Add SignalR ReleaseUpgradeHub" but the code took the HotChocolate-subscription route instead — ticket title is stale vs. implementation.) Verify end-to-end.
- **Billing limits partly unenforced.** `IsIngestAllowed` is gated (402 on ingest-session create), but `IsStorageAllowed`, `MaxStorageBytes`, and `IsEgressAllowed` are defined and (egress) metered yet have **no enforcement site**. Unclear whether this is intentional (commercial plugin would add gates) — confirm before assuming limits are enforced.
- **Stale references in repo docs.** `README.md` / root `CLAUDE.md` mention a `docs/` directory, an S3 chunk-store backend, the `Utils` tooling projects (`RepackFileDefs`, `ChunkStoreExplorer`, `RdefAnalyzer`), and `BinStash.Infrastructure.Tests` — **none exist on disk**. `BinStash.Infrastructure.csproj` still declares `InternalsVisibleTo` for those non-existent tooling projects.
- **`BinStash.StoreMigration`** has a `.csproj` on disk but is **not referenced by `BinStash.slnx`**, so it won't build with the solution; its currency against the present LSM/`.rdef` V6 format is unverified (it was a one-shot migration tool).
- **CLI dead dependencies**: `Microsoft.AspNetCore.DataProtection(.Extensions)` is still referenced in `BinStash.Cli.csproj` but unused (credentials now use `ProtectedData` / AES-256-GCM).
- **No `.editorconfig`** despite `.ai/AGENTS.md` saying "Respect `.editorconfig`"; style is governed only by the ReSharper `BinStash.sln.DotSettings`. **No central package management** → version drift across `.csproj` (e.g. `coverlet.collector` 8.0.1 vs 10.0.0).
- **Local-dev PostgreSQL provisioning is unverified** — there is no `compose.yaml`; older docs referencing `docker compose up` / Adminer no longer apply.
- **Stale code comment**: `FastCdcChunker` references "SHA256" in a comment but actually hashes with BLAKE3.
- **Older Plane tickets `BINST-1`…`BINST-40` were not reviewed** — the foundational feature history is only partially mapped (low priority; code is the truth).

## How To Close Them
- Run `dotnet build BinStash.slnx -c Release` and `dotnet test BinStash.slnx -c Release` to confirm compile state and real test counts.
- Grep for `IsStorageAllowed` / `IsEgressAllowed` / `MaxStorageBytes` usages and check `git log` for the SignalR→HotChocolate-subscriptions pivot and any later subscription wiring.
- Ask the user about intent for the `docs/` directory, the missing tooling projects, and the expected local-dev DB setup.
- When in doubt, query Plane (`mcp__plane__*`, project `BinStash` / identifier `BINST`) for ticket rationale, and verify against code before acting.

## Recently Resolved Gaps
- **CliFx 3.0 migration is complete** (older memory-bank notes claimed it was incomplete with LSP errors; commit `3476bb2`).
- **`.rdef` write format is V6** (older notes said V4; V5's storageKey keying was reverted to file-hash keying in V6).
- **`Subscription` *entity* was removed** (EF migration `RemoveSubscriptionEntity`); it is no longer in the schema.
- **Dockerfile targets net10** (`aspnet:10.0`/`sdk:10.0`) — Plane `CFG-01` (net9 images) is `Done`.
- **The frontend is in-repo** (`BinStash.Frontend`, builds into the server's `wwwroot`); older notes referenced an external `mosaic-vue` path.
