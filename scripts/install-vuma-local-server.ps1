[CmdletBinding()]
param(
    [string]$InstallRoot = "C:\Program Files\Vuma Retail\StoreServer",
    [string]$ServiceName = "VumaRetailStoreServer",
    [string]$DatabaseName = "vuma",
    [string]$DatabaseUser = "vuma",
    [string]$DatabasePassword,
    [int]$ApiPort = 7243,
    [string]$TenantId = "01900000-0000-7000-8000-0000000000d0",
    [switch]$InstallLocalPostgres,
    [switch]$Demo,
    [string]$BootstrapPassword
)

$ErrorActionPreference = "Stop"

if ($Demo) {
    Write-Warning "-Demo disables production security guards. Use only on an isolated development machine."
}

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this installer from an elevated PowerShell window."
}

if ($ApiPort -lt 1024 -or $ApiPort -gt 65535) {
    throw "ApiPort must be between 1024 and 65535."
}

$sourceRoot = $PSScriptRoot
$sourceExe = Join-Path $sourceRoot "VumaRetail.StoreServer.exe"
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "VumaRetail.StoreServer.exe was not found beside this script. Copy the complete windows-server bundle first."
}

if ([string]::IsNullOrWhiteSpace($DatabasePassword)) {
    $DatabasePassword = Read-Host "Local PostgreSQL password"
}
if ([string]::IsNullOrWhiteSpace($DatabasePassword)) {
    throw "A PostgreSQL password is required."
}

if ([string]::IsNullOrWhiteSpace($BootstrapPassword)) {
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $BootstrapPassword = -join (1..16 | ForEach-Object { $alphabet[(Get-Random -Maximum $alphabet.Length)] })
    Write-Host "[SAVE THIS — it will not be shown again] Bootstrap password: $BootstrapPassword"
}

New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
Copy-Item (Join-Path $sourceRoot "*") $InstallRoot -Recurse -Force

if ($InstallLocalPostgres) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker Desktop is required for -InstallLocalPostgres. Install Docker Desktop and rerun this script."
    }

    $container = docker ps -a --filter "name=^vuma-postgres$" --format "{{.Names}}"
    if (-not $container) {
        docker run -d --name vuma-postgres --restart unless-stopped `
            -e "POSTGRES_DB=$DatabaseName" `
            -e "POSTGRES_USER=$DatabaseUser" `
            -e "POSTGRES_PASSWORD=$DatabasePassword" `
            -p "127.0.0.1:5432:5432" postgres:17 | Out-Null
    } else {
        docker start vuma-postgres 2>$null | Out-Null
    }

    $ready = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $ready = (docker exec vuma-postgres pg_isready -U $DatabaseUser -d $DatabaseName 2>$null) -match "accepting connections"
        if ($ready) { break }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw "PostgreSQL did not become ready within 60 seconds." }
}

$machine = [EnvironmentVariableTarget]::Machine
[Environment]::SetEnvironmentVariable("ConnectionStrings__Vuma", "Host=127.0.0.1;Port=5432;Database=$DatabaseName;Username=$DatabaseUser;Password=$DatabasePassword", $machine)
[Environment]::SetEnvironmentVariable("Vuma__Host__TenantId", $TenantId, $machine)
[Environment]::SetEnvironmentVariable("Vuma__Host__StoreId", "", $machine)
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$JwtSigningKey = [Convert]::ToBase64String($bytes)
[Environment]::SetEnvironmentVariable("Vuma__Jwt__SigningKey", $JwtSigningKey, $machine)
[Environment]::SetEnvironmentVariable("VUMA_BOOTSTRAP_PASSWORD", $BootstrapPassword, $machine)
[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", $(if ($Demo) { "Development" } else { "Production" }), $machine)
[Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", "http://0.0.0.0:$ApiPort", $machine)

$exePath = Join-Path $InstallRoot "VumaRetail.StoreServer.exe"
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

& $exePath --migrate
& $exePath --seed

New-Service -Name $ServiceName -BinaryPathName ('"{0}"' -f $exePath) `
    -DisplayName "Vuma Retail Store Server" `
    -Description "Vuma Retail local API and database boundary." `
    -StartupType Automatic | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

New-NetFirewallRule -DisplayName "Vuma Retail StoreServer $ApiPort" `
    -Direction Inbound -Protocol TCP -LocalPort $ApiPort -Action Allow -Profile Private `
    -ErrorAction SilentlyContinue | Out-Null

Start-Service -Name $ServiceName
$serverName = [System.Net.Dns]::GetHostName()
Write-Host "Vuma local server installed and started."
Write-Host "Health check: http://localhost:$ApiPort/health"
Write-Host "Desktop API URL: http://$serverName`:$ApiPort/api/v1"
Write-Host "Generated JWT signing key (store securely): $JwtSigningKey"
Write-Host "Bootstrap login: admin / $BootstrapPassword"
Write-Host "After creating the real administrator, deactivate the bootstrap account."
