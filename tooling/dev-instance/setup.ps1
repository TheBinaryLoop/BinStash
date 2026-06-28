#Requires -Version 7
# Drives the BinStash first-run setup wizard over the REST API so the instance
# is immediately usable from the CLI — no frontend build required.
#
# Creates a Single-tenant instance with a local chunk store (under
# tooling/dev-instance/storage), one storage class, and an admin user
# (both instance + tenant admin).
#
# Get the setup code from the up.ps1 console ("SETUP CODE: XXXX-...").
#   pwsh tooling/dev-instance/setup.ps1 -Code XXXX-XXXX-XXXX-XXXX
param(
    [string]$BaseUrl        = 'https://localhost:7117',
    [string]$Code,
    [string]$AdminEmail     = 'admin@binstash.local',
    [string]$AdminPassword  = 'DevAdmin#12345',
    [string]$TenantName     = 'Dev',
    [string]$TenantSlug     = 'dev',
    [string]$ChunkStoreName = 'local',
    [string]$StorageClass   = 'standard'
)
$ErrorActionPreference = 'Stop'

$dev        = $PSScriptRoot
$chunksPath = Join-Path $dev 'storage/chunks'

if (-not $Code) { $Code = Read-Host 'Paste the SETUP CODE from the server log' }

# Dev cert may be untrusted in this shell; this stack is local-only.
$common = @{ ContentType = 'application/json'; SkipCertificateCheck = $true }

function Invoke-Setup([string]$Path, $Body, $Session) {
    Invoke-RestMethod "$BaseUrl/api/setup/$Path" @common -Method POST `
        -Body ($Body | ConvertTo-Json -Depth 6) -WebSession $Session
}

# --- wait for the server -----------------------------------------------------
Write-Host 'Waiting for the server to respond...' -ForegroundColor Cyan
$up = $false
for ($i = 0; $i -lt 60; $i++) {
    try { Invoke-RestMethod "$BaseUrl/api/setup/status" @common -Method GET | Out-Null; $up = $true; break }
    catch { Start-Sleep 1 }
}
if (-not $up) { throw "Server at $BaseUrl did not respond. Is up.ps1 running?" }

# --- 1. claim the setup code (issues the setup cookie) -----------------------
Invoke-RestMethod "$BaseUrl/api/setup/claim" @common -Method POST `
    -Body (@{ code = $Code } | ConvertTo-Json) -SessionVariable s | Out-Null
Write-Host 'Setup code accepted.' -ForegroundColor Green

# --- 2..N. drive the wizard --------------------------------------------------
Invoke-Setup 'tenancy'        @{ Mode = 'Single' }                                   $s | Out-Null
$tenant = Invoke-Setup 'default-tenant' @{ Name = $TenantName; Slug = $TenantSlug }  $s
$tenantId = $tenant.tenantId

# Type = 0 -> ChunkStoreType.Local
$cs = Invoke-Setup 'chunk-stores' @{ Type = 0; Name = $ChunkStoreName; LocalPath = $chunksPath } $s
$chunkStoreId = $cs.chunkStoreId

Invoke-Setup 'storage-class' @{
    StorageClasses = @(@{ Name = $StorageClass; DisplayName = 'Standard'; Description = 'Dev' })
} $s | Out-Null

Invoke-Setup 'storage/defaults' @{
    StorageClassDefaultMappings = @(@{
        ChunkStoreId = $chunkStoreId; StorageClassName = $StorageClass; IsDefault = $true; IsEnabled = $true
    })
} $s | Out-Null

Invoke-Setup 'admin' @{
    IsTenantAdmin = $true; IsInstanceAdmin = $true
    Email = $AdminEmail; Password = $AdminPassword; FirstName = 'Admin'; LastName = 'User'
} $s | Out-Null

Invoke-Setup 'finish' @{} $s | Out-Null

Write-Host ''
Write-Host 'Setup complete.' -ForegroundColor Green
Write-Host "  Admin user : $AdminEmail / $AdminPassword"
Write-Host "  Tenant     : $TenantSlug  ($tenantId)"
Write-Host "  Chunk store: $ChunkStoreName  ($chunkStoreId)"
Write-Host ''
Write-Host 'Log in with the CLI:' -ForegroundColor Cyan
Write-Host "  pwsh tooling/dev-instance/cli.ps1 auth login --url $BaseUrl"
