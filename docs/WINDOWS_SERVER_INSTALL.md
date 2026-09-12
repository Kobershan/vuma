# Windows StoreServer installation

This bundle installs the local Vuma StoreServer as a Windows Service. The service is configured to
start automatically at boot and to restart after a crash. If local PostgreSQL is installed through
the optional Docker switch, the PostgreSQL container uses `unless-stopped`, so it also returns after
power is restored and Docker starts.

## Build the bundle

Run from an elevated-capable development machine:

```powershell
pwsh scripts/build-windows-server-bundle.ps1
```

The bundle is created in `artifacts/windows-server` and contains:

- `VumaRetail.StoreServer.exe` — self-contained `win-x64` service executable;
- `install-vuma-storeserver.ps1` — service registration and configuration script.

## Install on the local server

Copy the bundle to the Windows server, open PowerShell as Administrator, and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-vuma-storeserver.ps1 `
  -InstallLocalPostgres `
  -TenantId 'TENANT-GUID' `
  -StoreId 'STORE-GUID' `
  -CloudBaseAddress 'https://api.example.com/' `
  -CloudAccessToken 'STORE_SPECIFIC_SYNC_TOKEN' `
  -JwtSigningKey 'LONG_RANDOM_STORE_KEY'
```

The script creates the `VumaRetailStoreServer` service, configures automatic startup, applies
database migrations, starts the service, and configures Windows Service recovery actions.

The installer stores connection values as machine-level environment variables. Protect the server,
restrict Administrator access, and rotate the sync token if the machine is compromised. Do not use
the development JWT key in production.

## Verify

```powershell
Get-Service VumaRetailStoreServer
docker ps
Invoke-WebRequest https://localhost/health
```

The cloud API must already be reachable over HTTPS and the store-specific sync token must have only
the required sync permission. PCs and mobile devices do not need PostgreSQL or cloud credentials.
