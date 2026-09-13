#!/usr/bin/env bash
set -euo pipefail
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT

printf 'release manifest\n' > "$test_root/manifest.txt"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$test_root/private.pem" >/dev/null 2>&1
openssl pkey -in "$test_root/private.pem" -pubout -out "$test_root/public.pem" >/dev/null 2>&1
openssl dgst -sha256 -sign "$test_root/private.pem" -out "$test_root/manifest.sig" "$test_root/manifest.txt"

"$(dirname "$0")/../verify-release-signature.sh" \
  "$test_root/manifest.txt" "$test_root/manifest.sig" "$test_root/public.pem" >/dev/null

printf 'tampered manifest\n' > "$test_root/manifest.txt"
if "$(dirname "$0")/../verify-release-signature.sh" \
  "$test_root/manifest.txt" "$test_root/manifest.sig" "$test_root/public.pem" >/dev/null 2>&1; then
  echo "tampered signed manifest was accepted" >&2
  exit 1
fi

echo "verify-release-signature tests passed"
