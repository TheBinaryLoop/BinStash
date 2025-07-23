# CLI Reference

BinStash CLI provides comprehensive control over chunk stores, repositories, and releases.

---

## `chunk-store`

Manage chunk stores (currently only supports local storage).

### Subcommands

- `add` — Add a new chunk store
    - `--name`, `-n`: Name (required)
    - `--type`, `-t`: Type (Local)
    - `--local-path`, `-p`: Local path for Local type
    - `--chunker-type`, `-c`: Chunker type (currently only supports FastCdc)
    - `--min-chunk-size`, `--avg-chunk-size`, `--max-chunk-size`: Chunking thresholds
- `list` — List all accessible chunk stores
- `show` — Show details about a specific chunk store
    - `--id`, `-i`: Chunk store GUID
- `delete` — [Not implemented]

---

## `repo`

Manage logical repositories for releases.

### Subcommands

- `add` — Add a new repository
    - `--name`, `-n`: Repository name
    - `--description`, `-d`: Optional description
    - `--chunk-store`, `-c`: Associated chunk store ID
- `list` — List all repositories

---

## `release`

Manage release lifecycles.

### Subcommands

- `add` — Create and upload a new release
    - `--version`, `-v`: Version identifier
    - `--repository`, `-r`: Target repository
    - `--folder`, `-f`: Folder with files
    - `--notes`, `-n`: Notes (optional)
    - `--notes-file`: File containing notes
    - `--component-map`, `-c`: Optional folder-to-component mapping file
- `list` — List all releases in a repository
    - `--repository`, `-r`: Repository name
- `install` — Download and extract a release
    - `--version`, `-v`: Release version
    - `--repository`, `-r`: Repo name (if using version)
    - `--release-id`, `-i`: Optional release GUID
    - `--component`, `-c`: Optional: extract only one component
    - `--target-folder`, `-t`: Extraction path
- `delete` — [Not implemented]

---

Run `BinStash.Cli [command] --help` for additional options and details.
