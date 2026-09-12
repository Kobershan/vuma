#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$InstallRoot = "C:\Program Files\Vuma Retail\StoreServer",
    [string]$ServiceName = "VumaRetailStoreServer",
    [string]$DatabaseName = "vuma",
    [string]$DatabaseUser = "vuma",
    [string]$DatabasePassword,
    [string]$CloudBaseAddress,
    [string]$CloudAccessToken,
    [string]$TenantId,
    [string]$StoreId,
    [string]$JwtSigningKey,
    [switch]$InstallLocalPostgres
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this installer from an elevated PowerShell window."
}

$sourceRoot = $PSScriptRoot
$sourceExe = Join-Path $sourceRoot "VumaRetail.StoreServer.exe"
if (-not (Test-Path $sourceExe)) {
    throw "VumaRetail.StoreServer.exe was not found beside this installer. Run build-windows-server-bundle.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($DatabasePassword)) {
    $DatabasePassword = Read-Host "Local PostgreSQL password"
}
if ([string]::IsNullOrWhiteSpace($CloudBaseAddress)) {
    $CloudBaseAddress = Read-Host "Cloud API base address (for example https://api.example.com/)"
}
if ([string]::IsNullOrWhiteSpace($CloudAccessToken)) {
    $CloudAccessToken = Read-Host "Store-specific cloud sync token"
}
if ([string]::IsNullOrWhiteSpace($TenantId) -or [string]::IsNullOrWhiteSpace($StoreId)) {
    throw "TenantId and StoreId are required; the store must never run as an unassigned node."
}
if ([string]::IsNullOrWhiteSpace($JwtSigningKey)) {
    $JwtSigningKey = Read-Host "Store JWT signing key"
}

New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
Copy-Item (Join-Path $sourceRoot "*") $InstallRoot -Recurse -Force

if ($InstallLocalPostgres) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker is required for -InstallLocalPostgres. Install Docker and rerun this script."
    }

    $existing = docker ps -a --filter "name=^vuma-postgres$" --format "{{.Names}}"
    if (-not $existing) {
        docker run -d --name vuma-postgres --restart unless-stopped `
            -e "POSTGRES_DB=$DatabaseName" `
            -e "POSTGRES_USER=$DatabaseUser" `
            -e "POSTGRES_PASSWORD=$DatabasePassword" `
            -p "127.0.0.1:5432:5432" postgres:16
    } else {
        docker start vuma-postgres 2>$null | Out-Null
    }
}

$machine = [EnvironmentVariableTarget]::Machine
[Environment]::SetEnvironmentVariable("ConnectionStrings__Vuma", "Host=127.0.0.1;Port=5432;Database=$DatabaseName;Username=$DatabaseUser;Password=$DatabasePassword", $machine)
[Environment]::SetEnvironmentVariable("Vuma__Host__TenantId", $TenantId, $machine)
[Environment]::SetEnvironmentVariable("Vuma__Host__StoreId", $StoreId, $machine)
[Environment]::SetEnvironmentVariable("Vuma__Jwt__SigningKey", $JwtSigningKey, $machine)
[Environment]::SetEnvironmentVariable("Vuma__Sync__Node__NodeId", "store:$StoreId", $machine)
[Environment]::SetEnvironmentVariable("Vuma__Sync__Peer__NodeId", "cloud", $machine)
[Environment]::SetEnvironmentVariable("Vuma__Sync__Peer__BaseAddress", $CloudBaseAddress, $machine)
[Environment]::SetEnvironmentVariable("Vuma__Sync__Peer__AccessToken", $CloudAccessToken, $machine)
[Environment]::SetEnvironmentVariable("Vuma__Sync__Dispatcher__Enabled", "true", $machine)

$exePath = Join-Path $InstallRoot "VumaRetail.StoreServer.exe"
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName -BinaryPathName ('"{0}"' -f $exePath) `
    -DisplayName "Vuma Retail Store Server" `
    -Description "Offline-first Vuma Retail store server and cloud synchronisation service." `
    -StartupType Automatic | Out-Null

sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

& $exePath --migrate
Start-Service -Name $ServiceName
Write-Host "Vuma Retail StoreServer installed and configured for automatic startup."
Write-Host "Service: $ServiceName"
Write-Host "Install root: $InstallRoot"
