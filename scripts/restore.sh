#!/usr/bin/env bash
# Restores a snapshot from the vault into a target database (R4).
#
# This is the "new box, run restore, back trading" path. It confirms the snapshot's SHA-256 before a
# single byte is restored — a restore that proceeded on a corrupted object would produce a database
# that looks restored and fails at the till, which is worse than not restoring at all, because the
# shop would stop looking for the real backup.
#
# Usage:
#   scripts/restore.sh "Host=localhost;Port=5432;Database=vuma_restored;Username=vuma;Password=..."
#   scripts/restore.sh "<target connection string>" <snapshot-id>
#
# With no snapshot id, the most recent completed snapshot is used.
#
# The target is required and has no default, deliberately: a restore replaces a database, and a
# command that defaulted to the live connection string would put "practise the restore" and "destroy
# the live store" one typo apart.
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: scripts/restore.sh <target connection string> [snapshot-id]" >&2
  exit 2
fi

target="$1"
snapshot="${2:-}"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

if [[ -n "${VUMA_CONNECTION:-}" ]]; then
  export ConnectionStrings__Vuma="$VUMA_CONNECTION"
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

args=(--restore "$target")
if [[ -n "$snapshot" ]]; then
  args+=(--snapshot "$snapshot")
fi

exec dotnet run --project "$root/src/VumaRetail.StoreServer" -c Release -- "${args[@]}"
