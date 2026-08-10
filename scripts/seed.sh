#!/usr/bin/env bash
# Builds the demonstrable demo tenant (docs/TESTING.md §5).
#
# Applies migrations, then creates the demo tenant, its stores, roles, users and a pending terminal.
# Idempotent: running it twice does not create a second copy of anything.
#
# Usage:
#   scripts/seed.sh                       # against ConnectionStrings:Zenith from appsettings
#   ZENITH_CONNECTION="..." scripts/seed.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

if [[ -n "${ZENITH_CONNECTION:-}" ]]; then
  export ConnectionStrings__Zenith="$ZENITH_CONNECTION"
fi

# The demo seed runs as a development tool by default. The host refuses to start on the placeholder
# JWT signing key outside Development (docs/SECURITY.md §1) and that guard stays exactly as it is —
# seeding a real deployment means setting ASPNETCORE_ENVIRONMENT and a real key, like any other run.
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

exec dotnet run --project "$root/src/ZenithRetail.StoreServer" -c Release -- --seed
