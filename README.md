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

## 📚 Documentation

- [Architecture](docs/architecture.md)
- [CLI Reference](docs/cli-reference.md)
- [Release Format Spec](docs/file-format.md)
- [Performance](docs/performance.md)
- [FAQ](docs/faq.md)

---

## 📜 License

BinStash is Copyright © 2025 Lukas Eßmann.

This program is licensed under the terms of the [GNU Affero General Public License v3.0](https://www.gnu.org/licenses/agpl-3.0.html).

---

> BinStash is for developers, CI/CD users, and artifact-heavy systems that value immutability, reproducibility, and efficient storage.