#Requires -Version 7
# Convenience wrapper to run the BinStash CLI from source against the dev server.
# Forwards all arguments to BinStash.Cli, e.g.:
#   pwsh tooling/dev-instance/cli.ps1 auth login --url https://localhost:7117
#   pwsh tooling/dev-instance/cli.ps1 chunk-store list --url https://localhost:7117
#   pwsh tooling/dev-instance/cli.ps1 repo add --name my-repo --chunk-store <id> --url https://localhost:7117 --tenant dev
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # repo root: tooling/dev-instance -> ..\..
dotnet run --project (Join-Path $root 'src/BinStash.Cli/BinStash.Cli.csproj') --no-launch-profile -- @args
