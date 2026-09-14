#!/usr/bin/env bash
set -euo pipefail
root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT
mkdir -p "$root/release/bin" "$root/release/.git" "$root/release/config"
printf payload > "$root/release/app.dll"
printf cache > "$root/release/bin/debug.dll"
printf secret > "$root/release/config/signing.key"
printf git > "$root/release/.git/config"
manifest="$root/manifest.txt"

"$(dirname "$0")/../generate-release-manifest.sh" "$root/release" "$manifest" >/dev/null
grep -q 'app.dll' "$manifest"
! grep -q 'debug.dll' "$manifest"
! grep -q 'signing.key' "$manifest"
! grep -q '.git/config' "$manifest"
"$(dirname "$0")/../verify-release-manifest.sh" "$manifest" "$root/release" >/dev/null
echo "generate-release-manifest tests passed"
