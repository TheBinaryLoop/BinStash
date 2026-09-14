# Enterprise and SaaS readiness

What BinStash still needs before it can be sold to enterprises and operated as a hosted
service, in the order it should be built.

This exists because the work is phased across many sessions and the *reasoning* behind each
item is the part that gets lost. Each entry says what is missing and why it matters, so the
next person — or the next agent — does not have to rederive the argument or re-litigate a
decision that has already been made.

**Status: Phase 1 complete** (PR #61). Phases 2–4 are open.

| Phase | Theme | State |
|---|---|---|
| 1 | Correctness and safety | ✅ done |
| 2 | Operability | open |
| 3 | Object storage and multi-replica | open |
| 4 | Enterprise gates | open |

Phase 1 and 2 are what separate *works* from *operable*. Phase 3 is what separates *one box*
from *a service*. Phase 4 is what separates *a service* from *a service enterprises will buy*.

---

## Phase 1 — correctness and safety ✅

Delivered in PR #61. Recorded here because the rest of the plan assumes it.

- Data Protection key ring persisted to the database (`SetApplicationName("BinStash")`). It was
  per-process and discarded on exit, so every auth cookie and Identity token died on restart.
- `/health/live` and `/health/ready` anonymous; the detailed report stays instance-admin. Nothing
  an orchestrator could present was previously able to probe the instance.
- Rate limiting on credential checks, mail-sending endpoints and ingest session creation.
- Explicit request-body ceilings, with a small one on `/api/auth`.
- Security headers and HSTS.
- Storage and egress quota enforced (`TenantQuotaGuard`), not merely computed.
- Egress metering checkpointed, so a disconnected download is no longer billed as nothing.
- Authentication and account-lifecycle events in the audit log.
- Per-tenant traffic recording and dashboards.

---

## Phase 2 — operability

Nothing here is architecturally hard. All of it is what makes the difference between an
instance that is running and an instance that can be *operated*.

### Observability

There is no OpenTelemetry, no metrics export, no tracing, and no structured log sink — just
`ILogger` to console. `RequestMetricsMiddleware` logs slow requests and that is the extent of
it. You cannot alert on ingest failure rate, GC outcomes, storage occupancy, DB pool
exhaustion, or per-tenant throughput.

Wanted: OTLP traces and metrics, RED metrics per endpoint and per tenant, and the counters the
domain already knows about (chunks written, dedup ratio, GC quarantined/reclaimed, ingest
session lifetimes). The traffic table added in Phase 1 covers bytes only — it is not a
substitute for instrumentation.

### Backup and restore

Nothing in the repository addresses this, and it is harder here than in most systems: the
PostgreSQL schema and the chunk store must be backed up **consistently with each other**. A
store without its rows is opaque; rows without their store are dangling references.

Wanted: a documented procedure, a point-in-time-consistency approach, a rehearsed restore, and
stated RPO/RTO. Until a restore has actually been performed, assume it does not work.

### Deployment artifacts

Today there is one `Dockerfile` with no `HEALTHCHECK`, no labels, no SBOM, no signing, and no
multi-arch build. The frontend is **not** built in-image — `COPY . .` silently depends on a
locally built `wwwroot`, which is not reproducible.

Wanted: an image that builds the frontend, a published registry, a Helm chart or at minimum a
production compose file, and a systemd unit that matches how it is actually deployed (see the
pve02/CT115 runbook).

### CI

One Windows Jenkins pipeline, no container build or publish stage, no release artifacts.
`.github/` holds only `dependabot.yml` and issue templates.

### Tests

There are no integration or end-to-end tests. `Server.Tests` uses EF Core InMemory, which
**does not reproduce Npgsql query translation** — this has already cost one runtime failure
that a full green suite did not catch (a projection into a named type before `GroupBy`). The
on-disk format has no round-trip test across versions, and nothing exercises CLI → gRPC → pack
→ download end to end.

Wanted: a Testcontainers-backed PostgreSQL suite for anything that issues a query, and one
end-to-end ingest/download test.

### Loose ends

- `BinStash.StoreMigration` is not in `BinStash.slnx`, so the tool the backward-compatibility
  guarantee depends on is neither built nor tested by CI.
- REST paths are unversioned and gRPC has no version negotiation. `VersionGateMiddleware`
  forces CLI upgrades but there is no reciprocal "server supports CLI N-1" contract.
- Migrations auto-apply at startup with no down-path, so rollback after a schema change is
  unsupported. This becomes urgent in Phase 3, where it also races across replicas.
- CORS registers a default policy only in Development. Fine while the console is same-origin;
  breaks the moment it is not.

---

## Phase 3 — object storage and multi-replica

The two genuinely architectural pieces. They are independent and can run in parallel.

### 3a. An object-storage backend

`ChunkStoreType` has exactly one member, `Local`. `LocalFolderChunkStoreStorage` writes packs
to a filesystem path with a memory-mapped LSM index and a process-local handle cache. Two
server replicas cannot safely share one store, and capacity is capped at one volume.

The CLI already advertises a backend that does not exist: `--type` help says `(Local, S3)`.

This is not an adapter. The pack/idx design translates, but the LSM index, the 4 GiB rotation
and the atomic temp-then-rename all assume POSIX semantics. Expect to rework the index layer,
not just implement `IChunkStoreStorage`.

### 3b. Multi-replica readiness

Nothing coordinates today — no leader election, no advisory locks, no distributed anything.

| Component | Failure with two or more replicas |
|---|---|
| `AddInMemorySubscriptions()` | Job progress only reaches clients on the replica that ran the job |
| `RebuildJobChannel`, `GcJobChannel`, the release-upgrade channel | In-process queues; startup resume means **every** replica re-runs the same job |
| `ChunkStoreGcSchedulerService` | Each replica schedules collection independently |
| `ChunkStoreStatsHostedService`, `TenantStorageStatsHostedService`, `ChunkStoreProbeService` | Duplicated work, contending writes |
| `db.Database.Migrate()` at startup | Racing migrations; also blocks rolling upgrades |

Wanted: a database-backed job queue with leasing (or PostgreSQL advisory locks), Redis-backed
GraphQL subscriptions, migrations moved to a gated init step, and single-flight guards on the
schedulers.

Two things are already replica-safe and should stay that way: the Data Protection key ring, and
traffic recording — its accumulate is an additive upsert and its fold and prune are idempotent,
specifically so traffic is not one more thing that needs a leader.

---

## Phase 4 — enterprise gates

Each of these is a hard blocker for some class of customer.

### SSO — OIDC and SAML

Zero references in the codebase. This is the first question on every enterprise procurement
checklist. Also missing: SCIM provisioning and IdP group → tenant-role mapping.

### SMTP

`EmailSettings` has a full `Smtp` shape, the GraphQL mutation persists host, port, username,
password and security, and the UI exposes it — but `EmailSenderImplementation.GetEmailProvider()`
has cases for `"Brevo"` and `"None"` only, so selecting SMTP throws *Unsupported email provider*.
An on-premises or air-gapped install cannot send mail at all, and it currently looks like a
working feature to anyone evaluating it. Small fix, disproportionate impact.

### Secrets

`DbConfigurationProvider` reads `InstanceSettings` as raw key/value text, so `Email:Brevo:ApiKey`
and `Email:Smtp:Password` sit in PostgreSQL in plaintext. The connection-string placeholder in
`appsettings.json` is literally `<from-keyvault>` with nothing implementing it.

Related and already live: the Data Protection key ring is stored **unencrypted** (`No XML
encryptor configured` at startup). Acceptable if the database is encrypted at rest;
`ProtectKeysWithCertificate` is the proper fix.

### JWT

HS256 with a symmetric key from configuration, `ValidateAudience = false`, no `kid`, no
rotation, no JWKS, and no access-token revocation path. Asymmetric signing with rotation is
table stakes for an enterprise security review.

### Encryption at rest and CMK

No crypto in the storage layer at all. Regulated customers will want customer-managed keys;
everyone else needs at minimum a documented encrypted-volume position.

### Audit export and retention

The trail is well built but goes nowhere: no SIEM export (syslog, webhook, S3), no
tamper-evidence, no retention policy.

### User and session administration

No `DisableUser`, `DeleteUser`, or session/token revocation. Password policy is whatever ASP.NET
Identity defaults to and is not operator-configurable. There is no way to lock out a departing
employee's active tokens.

### Documentation

`README.md` links `docs/architecture.md`, `docs/cli-reference.md`, `docs/file-format.md`,
`docs/performance.md` and `docs/faq.md`. This file is currently the only thing in `docs/`.
Also absent: install guide, ops runbook, security whitepaper, `SECURITY.md`, `CONTRIBUTING.md`,
`CHANGELOG.md`.

### Commercial and legal

`LICENSE-COMMERCIAL.md` is a stub with a placeholder contact address (`lukas.essmann@example.com`,
mirrored in `README.md`). There is no license-key or entitlement mechanism for **paid
self-hosted** — the billing plugin is a SaaS metering path, not an entitlement check — so there
is currently no way to sell or enforce a commercial on-premises licence. No ToS, DPA, privacy
policy, SLA, sub-processor list or security whitepaper.

### Compliance

For SOC 2 / ISO 27001: audit logging and vulnerability management are partly there; access
reviews, change management, penetration testing and incident response are not. Single region
only, so there is no data-residency story.

---

## Decisions already made

Recorded so they are not re-argued.

- **Traffic storage lives in the AGPL core**, not behind the billing plugin. Knowing how much
  traffic an instance is moving is operational visibility, not billing; a deployment with no
  billing provider still has to answer why the link saturated. `IUsageMeteringService` remains a
  separate call that a commercial plugin can make durable on its own terms.
- **Traffic retention**: hourly for 30 days, folded to daily, daily kept 13 months.
- **Storage quota is enforced against logical bytes**, matching what a tenant is billed on.
  Physical footprint is a property of the shared chunk store and is neither billable per tenant
  nor safe to report to one.
- **The analysis endpoint requires repository Write**, not Read. Answering "does this store hold
  this chunk" is an oracle over content another tenant may own; that oracle is already reachable
  by anyone who can ingest, and it must not be widened beyond them.
- **A hard process kill loses up to one flush interval of traffic.** Deliberate: recording every
  chunk upload synchronously would put a database write on the ingest hot path. Revisit only if
  traffic ever needs to be invoice-grade.

---

## Known non-goals for now

- Global deduplication across chunk stores. Dedup scope is intentionally per chunk store.
- Microservices. The modular monolith is a deliberate choice; Phase 3 is about running more than
  one copy of it, not splitting it.
