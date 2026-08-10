#!/usr/bin/env bash
# The disaster-recovery drill: seed → snapshot → restore into a new database → compare (R4).
#
# R4 is not "backups are taken". It is "store burns down → new box, run restore, back trading",
# which is a claim about a RESTORE and can only be supported by having done one. This script does
# one, against a throwaway PostgreSQL cluster, end to end, in about a minute.
#
#   scripts/dr-drill.sh
#
# Stage 31 owns the full exercise — a bare Windows machine, the installer, the service, the tills
# reconnecting. This is the part of it that can be run on any developer's laptop and in CI, which is
# the part that will actually be run often enough to keep working.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

started_cluster=0
if [[ -z "${VUMA_TEST_POSTGRES:-}" ]]; then
  eval "$("$root/scripts/pg-test.sh" start)"
  started_cluster=1
fi
export VUMA_TEST_POSTGRES

cleanup() {
  if [[ "$started_cluster" == "1" ]]; then
    "$root/scripts/pg-test.sh" stop >/dev/null 2>&1 || true
  fi
  rm -rf "$vault" 2>/dev/null || true
}
trap cleanup EXIT

vault="$(mktemp -d)"
# A throwaway key. A real deployment supplies this as a secret and never commits it; the drill needs
# only that the snapshot is genuinely encrypted, not that the key is the production one.
key="$(head -c 32 /dev/urandom | base64)"

source_db="vuma_drill_source"
target_db="vuma_drill_restored"

# VUMA_TEST_POSTGRES is an ADO.NET connection string; psql speaks libpq. Pull the pieces out once
# and build both forms from them, rather than translating at every call site.
field() { echo "$VUMA_TEST_POSTGRES" | tr ';' '\n' | awk -F= -v k="$1" 'tolower($1)==tolower(k){print $2}'; }

pg_host="$(field Host)"
pg_port="$(field Port)"
pg_user="$(field Username)"
pg_pass="$(field Password)"
export PGPASSWORD="$pg_pass"

psql_for() { psql -h "$pg_host" -p "$pg_port" -U "$pg_user" -d "$1" -v ON_ERROR_STOP=1 -q -t -A "${@:2}"; }
psql_admin() { psql -h "$pg_host" -p "$pg_port" -U "$pg_user" -d postgres -v ON_ERROR_STOP=1 -q -c "$1"; }
conn_for() { echo "Host=$pg_host;Port=$pg_port;Database=$1;Username=$pg_user;Password=$pg_pass"; }

echo "==> preparing two databases"
psql_admin "DROP DATABASE IF EXISTS $source_db WITH (FORCE)"
psql_admin "DROP DATABASE IF EXISTS $target_db WITH (FORCE)"
psql_admin "CREATE DATABASE $source_db"
psql_admin "CREATE DATABASE $target_db"

source_conn="$(conn_for "$source_db")"
target_conn="$(conn_for "$target_db")"

export ConnectionStrings__Vuma="$source_conn"
export Vuma__Backup__Vault__Provider="FileSystem"
export Vuma__Backup__Vault__Directory="$vault"
export Vuma__Backup__Encryption__Key="$key"

run_host() {
  dotnet run --project "$root/src/VumaRetail.StoreServer" -c Release -- "$@"
}

echo "==> migrating and seeding the source store"
run_host --migrate
run_host --seed >/dev/null

# The seed sets the demo tenant; the host has to be pointed at it for the snapshot to have a tenant.
demo_tenant="$(psql_for "$source_db" -c 'SELECT id FROM platform.tenants LIMIT 1')"
export Vuma__Host__TenantId="$demo_tenant"

count_of() { psql_for "$1" -c "SELECT count(*) FROM $2"; }

users_before="$(count_of "$source_db" 'identity.users')"
roles_before="$(count_of "$source_db" 'identity.roles')"
stores_before="$(count_of "$source_db" 'platform.stores')"

echo "    users=$users_before roles=$roles_before stores=$stores_before"

echo "==> taking a snapshot"
snapshot_line="$(run_host --backup | grep '^snapshot ')"
snapshot_id="$(echo "$snapshot_line" | awk '{print $2}')"
echo "    $snapshot_line"

echo "==> confirming the object is encrypted"
object="$(find "$vault" -name '*.vsnap' | head -1)"
if grep -aq "demo" "$object"; then
  echo "FAIL: the snapshot contains readable tenant data" >&2
  exit 1
fi
echo "    $(basename "$object") — no readable tenant data"

echo "==> verifying the snapshot reads back intact"
run_host --verify-backup --snapshot "$snapshot_id"

echo "==> restoring into a database that has never had a schema"
run_host --restore "$target_conn" --snapshot "$snapshot_id"

echo "==> comparing"
users_after="$(count_of "$target_db" 'identity.users')"
roles_after="$(count_of "$target_db" 'identity.roles')"
stores_after="$(count_of "$target_db" 'platform.stores')"

echo "    users=$users_after roles=$roles_after stores=$stores_after"

failed=0
[[ "$users_before"  == "$users_after"  ]] || { echo "FAIL: users $users_before -> $users_after" >&2; failed=1; }
[[ "$roles_before"  == "$roles_after"  ]] || { echo "FAIL: roles $roles_before -> $roles_after" >&2; failed=1; }
[[ "$stores_before" == "$stores_after" ]] || { echo "FAIL: stores $stores_before -> $stores_after" >&2; failed=1; }

if [[ "$failed" == "1" ]]; then
  echo "DR DRILL FAILED" >&2
  exit 1
fi

echo
echo "DR DRILL PASSED — snapshot $snapshot_id restored into $target_db with matching row counts."
