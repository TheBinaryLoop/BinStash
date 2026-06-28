# Local dev instance

A self-contained, disposable BinStash dev environment. PostgreSQL runs in Docker;
the server runs from source over HTTPS on `:7117`. Everything the instance creates
at runtime lives under this folder or in the `binstash-dev` Docker project, so
cleanup is a single script.

The harness itself (these scripts + `.env` + `compose.yaml`) is committed. Only the
**runtime artifacts** it generates are gitignored — see [Notes](#notes).

> Run the scripts from the repo root, e.g. `pwsh tooling/dev-instance/up.ps1`.
> They locate the repo root relative to their own location, so the working
> directory doesn't matter.

## Prerequisites

- Docker (for PostgreSQL)
- .NET 10 SDK
- A trusted HTTPS dev cert (once per machine). The CLI talks REST **and** gRPC
  over TLS and does not skip cert validation:

  ```pwsh
  dotnet dev-certs https --trust
  ```

## Start it

**Terminal A** — Postgres + server (stays in the foreground):

```pwsh
pwsh tooling/dev-instance/up.ps1
```

On the **first** run the server prints a one-time line like
`SETUP CODE: ABCD-EFGH-JKLM-NPQR`. Copy it.

**Terminal B** — initialize the instance (only needed once):

```pwsh
pwsh tooling/dev-instance/setup.ps1 -Code ABCD-EFGH-JKLM-NPQR
```

This creates a **Single-tenant** instance with:

- tenant `dev`
- local chunk store `local` at `tooling/dev-instance/storage/chunks`
- storage class `standard` (default)
- admin user `admin@binstash.local` / `DevAdmin#12345`

(Override any of these via `setup.ps1` parameters.)

## Use it

```pwsh
# Log in (stores a token in %APPDATA%\BinStash\Cli\auth.dat)
pwsh tooling/dev-instance/cli.ps1 auth login --url https://localhost:7117

# Explore
pwsh tooling/dev-instance/cli.ps1 chunk-store list --url https://localhost:7117
pwsh tooling/dev-instance/cli.ps1 repo list       --url https://localhost:7117 --tenant dev

# Add a repo (maps to the chunk store via the 'standard' storage class created during setup)
pwsh tooling/dev-instance/cli.ps1 repo add --name my-repo --storage-class standard `
    --url https://localhost:7117 --tenant dev

# Publish a release from a folder
pwsh tooling/dev-instance/cli.ps1 release add -v 1.0.0 -r my-repo -f .\some\build_output `
    --url https://localhost:7117 --tenant dev
```

API docs (Scalar): <https://localhost:7117/scalar> · GraphQL: <https://localhost:7117/graphql>

## Restarting

Just stop the server (Ctrl+C in Terminal A) and run `pwsh tooling/dev-instance/up.ps1`
again. Setup is **not** repeated — the DB persists in the Docker volume, and the
server reloads the tenancy mode + default tenant id it stored during setup.

## Clean up / remove

```pwsh
pwsh tooling/dev-instance/down.ps1                 # drop Postgres container + volume + storage/
pwsh tooling/dev-instance/cli.ps1 auth logout-all  # clear stored CLI tokens (optional)
```

`down.ps1` removes the running instance only; the committed harness scripts stay in
place and the storage it deletes is regenerated on the next `up.ps1` + `setup.ps1`.

## Notes

- **Gitignored runtime artifacts:** `tooling/dev-instance/storage/` (the local
  chunk store). Everything else in this folder is committed. `.env` is committed
  on purpose — it holds only throwaway local-dev Postgres credentials.
- Config is injected via environment variables in `up.ps1` and overrides
  `appsettings*.json` — the checked-in config files are not modified.
- `Storage:AllowedRootPath` is pinned to `tooling/dev-instance/storage`, so any
  chunk store you add via the CLI must live under that path.
- The JWT signing key is a fixed dev-only value so CLI tokens survive restarts.
  Do not reuse this setup for anything public.
