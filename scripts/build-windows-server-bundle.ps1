#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/windows-server"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root $OutputPath

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

dotnet publish (Join-Path $root "src/VumaRetail.StoreServer/VumaRetail.StoreServer.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableWindowsTargeting=true `
    -o $output

Copy-Item (Join-Path $root "scripts/install-vuma-storeserver.ps1") $output
Write-Host "Windows server bundle created at $output"
