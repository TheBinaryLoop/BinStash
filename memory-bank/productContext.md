# Product Context

## Why BinStash exists

Large CI/CD pipelines repeatedly store near-identical build artifacts — the same binaries with minor version differences. Conventional artifact stores treat each upload as independent, wasting disk space and bandwidth. BinStash solves this by applying content-defined chunking at the sub-file level: only new chunks are stored, regardless of file size or name.

## Problems it solves

1. **Redundant storage** — Identical byte ranges across releases are stored exactly once per chunk store.
2. **Slow artifact downloads** — Delta downloads let clients fetch only chunks they don't already have.
3. **Opaque artifact management** — GraphQL and REST APIs expose repositories, releases, and storage metrics in a structured, queryable way.
4. **CI/CD integration complexity** — The CLI provides a simple interface (`release add`, `release install`) that fits naturally into pipeline steps.

## How it works (user perspective)

1. Operator sets up a server with PostgreSQL and a local chunk store.
2. Developer/CI runner uses `BinStash.Cli release add` pointing at a build output directory.
3. CLI chunks all files (FastCDC), computes BLAKE3 hashes, uploads only missing chunks via gRPC streaming.
4. CLI uploads a `.rdef` release definition file; server registers the release in the database.
5. Consumers run `release install` to download a release (full or delta) to a local directory.

## User experience goals

- CLI commands are simple and composable with standard shell tooling.
- Deduplication is transparent — users declare a release; the system handles storage efficiency automatically.
- GraphQL API allows management tooling and dashboards to be built on top.
- Multi-tenant mode supports multiple teams/customers sharing one server.

## Business / operational outcomes

- Reduced storage costs in CI artifact stores.
- Faster pipeline cycle times through delta download support.
- Reproducible, immutable release packages linked to specific chunk states.
