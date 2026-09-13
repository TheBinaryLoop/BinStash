# BinStash: Smart Storage for Your Builds. Chunked, Deduplicated, Compressed.

![License: AGPLv3](https://img.shields.io/badge/license-AGPLv3-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)
![Status](https://img.shields.io/badge/status-alpha-orange)

BinStash is a modern tool for efficiently storing build artifacts and release packages. It uses content-defined chunking, deduplication, and a custom binary format to dramatically reduce redundant storage, especially in CI/CD pipelines.

---

## 🚀 Quickstart

```bash
# 1. Setup your server with PostgreSQL and blob storage
# 2. Add a chunk store and repo
BinStash.Cli chunk-store add --name my-store --type Local --local-path /mnt/data
BinStash.Cli repo add --name my-repo --chunk-store <chunk-store-id>

# 3. Add your first release
BinStash.Cli release add -v 1.0.0 -r my-repo -f ./build_output
```

---

## ✨ Features

- 🧩 **FastCDC Chunking** — Configurable, content-defined chunking for optimized diffs.
- 💾 **Deduplication** — Chunk-based deduplication across all releases in the same store.
- 📦 **Custom Binary Format** — Delta-encoded, varint-packed, transpose-compressed metadata.
- 🪶 **Zstd Compression** — Transparent compression for both chunk data and metadata.
- 🔁 **Partial/Delta Downloads** — Download full releases, components, or deltas.
- 🧠 **Pack-Based Chunk Storage** — Efficient on-disk structure with indexed `.pack` files.
- 🧹 **Online Garbage Collection** — Reclaims unreferenced content while the store stays readable and writable.

---

## ⚙️ Build & Install

- **Language**: [.NET 10](https://dotnet.microsoft.com/)
- **Dependencies**:
    - [CliFx](https://github.com/Tyrrrz/CliFx)
    - [Spectre.Console](https://spectreconsole.net/)
    - Custom fork of [ZstdNet](https://github.com/TheBinaryLoop/ZstdNetNGX)

Build with:

```bash
dotnet build --configuration Release
```

---

## 🧰 Local development

Spin up a throwaway local instance — disposable PostgreSQL in Docker plus the server
running from source — with the harness under
[`tooling/dev-instance/`](tooling/dev-instance/README.md):

```pwsh
pwsh tooling/dev-instance/up.ps1      # terminal A: Postgres + server on https://localhost:7117
pwsh tooling/dev-instance/setup.ps1   # terminal B: one-time first-run setup wizard
```

See [`tooling/dev-instance/README.md`](tooling/dev-instance/README.md) for the full flow,
CLI examples, and cleanup.

---

## 🧪 CLI Overview

```bash
BinStash.Cli [options]
BinStash.Cli [command] [...]
```

- `chunk-store`: Add/list/show/delete chunk stores
- `repo`: Create/list repositories for organizing releases
- `release`: Add, list, install, and manage deduplicated releases
- `analyze`: Tune chunking and deduplication strategies

See [`docs/cli-reference.md`](docs/cli-reference.md) for details.

---

## 🧱 Storage Internals

### 📂 Chunk Storage

- Chunks are stored in `.pack` files grouped by BLAKE3 prefix
- Each `.pack` entry includes:
    - Magic header (`BSPK`)
    - Compressed + uncompressed length
    - xxHash3 checksum
- An `.idx` file tracks chunk offset and length for fast reads
- Files rotate at 4 GiB for performance and portability

### 📂 File Definition Storage

- Chunks are stored in `.pack` files grouped by BLAKE3 prefix
- Each `.pack` entry includes:
    - Magic header (`BSPK`)
    - Compressed + uncompressed length
      - Data: Transpose-compressed chunk-hashes
    - xxHash3 checksum
- An `.idx` file tracks chunk offset and length for fast reads
- Files rotate at 4 GiB for performance and portability

### 🧬 Release Format

`.rdef` files include:
- Transpose-compressed file hash table
- Tokenized component + file name strings
- Zstd compression on all sections

More: [`docs/file-format.md`](docs/file-format.md)

---

## 🧹 Garbage Collection

Not every byte a chunk store holds is still referenced. An upload that never finished leaves
orphaned chunks behind, and deleting a release leaves whatever it was the last reference to.
Garbage collection finds that content and gives the space back.

Collection runs **online**: the store is never taken offline, and uploads and downloads continue
against it throughout. It works in four phases — snapshot, mark, sweep, reclaim — and separates
*finding* unreachable content from *destroying* it:

| Phase | What it does |
|-------|--------------|
| Snapshot | Records each bucket's append position, so content written after the run started is never a candidate. |
| Mark | Walks every release to the chunks and file definitions it reaches. |
| Sweep | Quarantines everything the mark phase did not reach. **Reversible** — the bytes are still on disk. |
| Reclaim | Physically drops content whose quarantine has expired. The only irreversible step. |

Quarantine is what makes this safe to run against live traffic. A quarantined object stops being
deduplicated against, so new uploads simply re-upload it; an upload already in flight that was
told "this already exists" recovers its reference when it finishes. Nothing is destroyed until the
retention window has passed **and** no ingest session older than the quarantine is still running.

### Running it

From **Instance → Chunk stores → *store* → Collect garbage**, or via GraphQL:

```graphql
mutation {
  collectChunkStoreGarbage(chunkStoreId: "…", dryRun: true) {
    id
    status
  }
}
```

Two non-destructive modes are available for getting comfortable with what a run would do:

- **Report only** (`dryRun`) — measures what would be collected and changes nothing.
- **Quarantine only** (`skipReclaim`) — hides unreachable content but destroys nothing, leaving
  reclamation as a separate, deliberate step.

### Running it on a schedule

Unattended collection is configured under **Instance → Settings → Collection**, and is **disabled
by default**: collection destroys unreachable content once its quarantine expires, so an instance
has to opt in rather than inherit it from an upgrade.

| Setting | Meaning |
|---------|---------|
| Collect every | How long since the last run before a store is collected again. |
| Keep recoverable for | Quarantine retention. Must comfortably outlast your longest upload. |
| Only start during set hours | Optional UTC window for *starting* a run. A run in progress is never interrupted. |
| Report only / Quarantine only | Apply the non-destructive modes above to scheduled runs. |

The settings are stored per instance and take effect without a restart. They can also be supplied
through the `ChunkStoreGc` configuration section — as `appsettings.json` or environment variables —
which takes precedence over the UI, so a deployment can pin a value operators must not change:

```jsonc
{
  "ChunkStoreGc": {
    "RetentionWindow": "24:00:00",
    "Schedule": {
      "Enabled": true,
      "Interval": "24:00:00",
      "WindowStartHourUtc": 22,
      "WindowEndHourUtc": 4
    }
  }
}
```

Progress and results are reported per run on the chunk store's page. Quarantined, dropped and
freed-on-disk are tracked separately on purpose: compaction rewrites a pack file's survivors into a
new file, so the volume only shrinks once the superseded file is unlinked, which is usually a later
run's doing.

---

## 📚 Documentation

- [Architecture](docs/architecture.md)
- [CLI Reference](docs/cli-reference.md)
- [Release Format Spec](docs/file-format.md)
- [Performance](docs/performance.md)
- [FAQ](docs/faq.md)

---

## 📜 License

BinStash is Copyright © 2025 Lukas Eßmann.

BinStash is dual-licensed:
- For open-source use, it's licensed under the terms of the [GNU Affero General Public License v3.0](https://www.gnu.org/licenses/agpl-3.0.html).
- For commercial or SaaS use without AGPLv3 obligations, a commercial license is available. See [LICENSE-COMMERCIAL.md](LICENSE-COMMERCIAL.md) or contact lukas.essmann@example.com for details.

---

> BinStash is for developers, CI/CD users, and artifact-heavy systems that value immutability, reproducibility, and efficient storage.