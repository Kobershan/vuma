#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$PublishPath = "artifacts/windows-server",
    [string]$OutputPath = "artifacts/installer"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = [IO.Path]::GetFullPath((Join-Path $root $PublishPath))
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputPath))

if (-not (Test-Path -LiteralPath (Join-Path $publish "VumaRetail.StoreServer.exe"))) {
    & (Join-Path $PSScriptRoot "build-windows-server-bundle.ps1") -Configuration $Configuration -OutputPath $PublishPath
    if ($LASTEXITCODE -ne 0) { throw "The Windows server bundle failed to build." }
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
dotnet build (Join-Path $root "deploy/installer/VumaRetail.Installer.wixproj") `
    -c $Configuration `
    -p:PublishDir="$publish" `
    -p:OutputPath="$output\" `
    -p:RunWixToolsOutOfProc=true
if ($LASTEXITCODE -ne 0) { throw "The WiX installer failed to build." }

Write-Host "Vuma Retail installer created under $output"
