# Vuma Retail Windows installer

This directory is the checked-in WiX v4 source for the first-install MSI. It packages the
self-contained `VumaRetail.StoreServer.exe`, registers the Windows service, starts it on install,
and stops/removes it during upgrade or uninstall. The service's PostgreSQL data is deliberately
outside the install directory, so uninstall does not delete business data.

Build from a Windows runner with the .NET SDK and WiX NuGet toolchain available:

```powershell
pwsh scripts/build-windows-installer.ps1
```

The script first creates `artifacts/windows-server` when needed and writes the MSI beneath
`artifacts/installer`. Signing is intentionally a separate release step: no certificate or private
key belongs in this repository. Production installation still requires provisioning TLS,
PostgreSQL, backup encryption, and the machine-level secrets described in
`docs/WINDOWS_SERVER_INSTALL.md`.
