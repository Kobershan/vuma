#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "artifacts/windows-server"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputPath))

# This script deletes its destination before publishing. Keep that destructive operation inside
# the repository's dedicated artifact tree, and reject links so a local build cannot escape it.
$artifactPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($output -eq $artifactRoot -or -not $output.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must resolve to a child of $artifactRoot."
}

$rootPath = [IO.Path]::GetPathRoot($output)
if ($output -eq $root -or $output -eq $rootPath -or $output -eq [Environment]::GetFolderPath("UserProfile")) {
    throw "Refusing to use a broad filesystem location as the bundle output."
}

$existing = Get-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
if ($null -ne $existing -and $existing.LinkType) {
    throw "Refusing to delete a symlink or junction at $output."
}

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
