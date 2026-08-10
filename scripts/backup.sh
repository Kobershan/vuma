#!/usr/bin/env bash
# Takes an encrypted snapshot of the store database and puts it in the configured vault (R4).
#
# Runs through the store server's own IBackupService, so the object is encrypted, checksummed and
# recorded on the ledger exactly as it is when the API takes one. A script that shelled out to
# pg_dump directly would be a second implementation of the one operation nobody gets to practise.
#
# Usage:
#   scripts/backup.sh
#   VUMA_CONNECTION="..." scripts/backup.sh
#
# Configuration (appsettings or environment):
#   Vuma__Backup__Vault__Provider     FileSystem | S3
#   Vuma__Backup__Vault__Directory    where a filesystem vault keeps its objects
#   Vuma__Backup__Encryption__Key     256-bit base64. Required — a snapshot is every row a tenant has.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

if [[ -n "${VUMA_CONNECTION:-}" ]]; then
  export ConnectionStrings__Vuma="$VUMA_CONNECTION"
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

exec dotnet run --project "$root/src/VumaRetail.StoreServer" -c Release -- --backup
