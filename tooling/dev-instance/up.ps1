#Requires -Version 7
# Starts a local BinStash dev instance:
#   1. brings up the disposable PostgreSQL container (waits until healthy),
#   2. runs the BinStash.Server against it over HTTPS on :7117.
#
# First run prints a one-time "SETUP CODE: ..." in this console — copy it and
# run setup.ps1 from a second terminal to initialize the instance.
#
# Stop the server with Ctrl+C. The Postgres container keeps running; tear the
# whole thing down with down.ps1.
$ErrorActionPreference = 'Stop'

$dev  = $PSScriptRoot
$root = Split-Path (Split-Path $dev -Parent) -Parent   # repo root: tooling/dev-instance -> ..\..

# --- load .env ---------------------------------------------------------------
$cfg = @{}
Get-Content (Join-Path $dev '.env') |
    Where-Object { $_ -match '^\s*[^#].*=' } |
    ForEach-Object {
        $k, $v = $_ -split '=', 2
        $cfg[$k.Trim()] = $v.Trim()
    }

# --- disposable PostgreSQL ---------------------------------------------------
Write-Host 'Starting PostgreSQL (docker compose)...' -ForegroundColor Cyan
docker compose -f (Join-Path $dev 'compose.yaml') up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed to start PostgreSQL.' }

# --- chunk-store storage root ------------------------------------------------
$storage = Join-Path $dev 'storage'
New-Item -ItemType Directory -Force -Path (Join-Path $storage 'chunks') | Out-Null

# --- HTTPS dev cert (the CLI talks REST + gRPC over TLS and won't bypass it) --
dotnet dev-certs https --check --quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host 'HTTPS dev certificate is not trusted. Run once:' -ForegroundColor Yellow
    Write-Host '    dotnet dev-certs https --trust' -ForegroundColor Yellow
}

# --- server configuration via environment (overrides appsettings) ------------
$env:ASPNETCORE_ENVIRONMENT       = 'Development'
$env:ASPNETCORE_URLS              = 'https://localhost:7117'
$env:ConnectionStrings__BinStashDb = "Host=localhost;Port=$($cfg['POSTGRES_PORT']);Database=$($cfg['POSTGRES_DB']);Username=$($cfg['POSTGRES_USER']);Password=$($cfg['POSTGRES_PASSWORD'])"
$env:Storage__AllowedRootPath     = $storage
# Stable signing key so CLI tokens survive a server restart (dev only).
$env:Auth__Jwt__Key               = 'binstash-dev-only-insecure-signing-key-0123456789abcdef'

# No tenancy env vars needed: the server persists Tenancy:Mode + Tenancy:DefaultTenantId
# to the database during setup and reloads them on every start, so single-tenant
# resolution survives restarts on its own.

Write-Host ''
Write-Host 'BinStash server:  https://localhost:7117' -ForegroundColor Green
Write-Host '  API docs:       https://localhost:7117/scalar' -ForegroundColor Green
Write-Host '  GraphQL:        https://localhost:7117/graphql' -ForegroundColor Green
Write-Host '  First run only: copy the "SETUP CODE" below, then run setup.ps1 in another terminal.' -ForegroundColor Green
Write-Host ''

dotnet run --project (Join-Path $root 'src/BinStash.Server/BinStash.Server.csproj') --no-launch-profile
