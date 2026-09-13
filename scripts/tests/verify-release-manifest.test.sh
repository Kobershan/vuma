#!/usr/bin/env bash
set -euo pipefail
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT
mkdir -p "$test_root/payload"
printf 'release payload\n' > "$test_root/payload/app.txt"
hash=$(sha256sum "$test_root/payload/app.txt" | awk '{print $1}')
printf '%s  payload/app.txt\n' "$hash" > "$test_root/manifest.txt"

"$(dirname "$0")/../verify-release-manifest.sh" "$test_root/manifest.txt" "$test_root" >/dev/null

printf 'tampered\n' > "$test_root/payload/app.txt"
if "$(dirname "$0")/../verify-release-manifest.sh" "$test_root/manifest.txt" "$test_root" >/dev/null 2>&1; then
  echo "tampered payload was accepted" >&2
  exit 1
fi

if "$(dirname "$0")/../verify-release-manifest.sh" <(printf '%064d  ../outside.txt\n' 0) "$test_root" >/dev/null 2>&1; then
  echo "path traversal manifest was accepted" >&2
  exit 1
fi

if "$(dirname "$0")/../verify-release-manifest.sh" <(printf '# comments only\n') "$test_root" >/dev/null 2>&1; then
  echo "empty manifest was accepted" >&2
  exit 1
fi

printf 'outside payload\n' > "$test_root/outside.txt"
ln -s ../outside.txt "$test_root/payload/linked.txt"
linked_hash=$(sha256sum "$test_root/outside.txt" | awk '{print $1}')
if "$(dirname "$0")/../verify-release-manifest.sh" <(printf '%s  payload/linked.txt\n' "$linked_hash") "$test_root" >/dev/null 2>&1; then
  echo "symlink payload was accepted" >&2
  exit 1
fi
echo "verify-release-manifest tests passed"
