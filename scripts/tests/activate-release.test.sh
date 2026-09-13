#!/usr/bin/env bash
set -euo pipefail
root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT
mkdir -p "$root/releases/one" "$root/releases/two"
printf one > "$root/releases/one/version"
printf two > "$root/releases/two/version"
active="$root/active"
rollback="$root/rollback"

"$(dirname "$0")/../activate-release.sh" "$root/releases/one" "$active" "$rollback" >/dev/null
[ "$(cat "$active/version")" = one ]
"$(dirname "$0")/../activate-release.sh" "$root/releases/two" "$active" "$rollback" >/dev/null
[ "$(cat "$active/version")" = two ]
[ "$(readlink "$rollback")" = "$root/releases/one" ]
"$(dirname "$0")/../activate-release.sh" unused "$active" "$rollback" --rollback >/dev/null
[ "$(cat "$active/version")" = one ]
echo "activate-release tests passed"
