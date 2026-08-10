<#
.SYNOPSIS
    Builds the demonstrable demo tenant (docs/TESTING.md §5).
.DESCRIPTION
    Applies migrations, then creates the demo tenant, its stores, roles, users and a pending
    terminal. Idempotent: running it twice does not create a second copy of anything.

    This is the Windows entry point CLAUDE.md §8 names. scripts/seed.sh is the same thing for the
    Linux build machine.
.PARAMETER ConnectionString
    Overrides ConnectionStrings:Zenith from appsettings.
#>
[CmdletBinding()]
param(
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($ConnectionString) {
    $env:ConnectionStrings__Zenith = $ConnectionString
}

# The demo seed runs as a development tool by default. The host refuses to start on the placeholder
# JWT signing key outside Development (docs/SECURITY.md §1); seeding a real deployment means setting
# ASPNETCORE_ENVIRONMENT and a real signing key, like any other run.
if (-not $env:ASPNETCORE_ENVIRONMENT) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
}

dotnet run --project (Join-Path $root 'src/ZenithRetail.StoreServer') -c Release -- --seed
