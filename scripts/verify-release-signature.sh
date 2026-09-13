#!/usr/bin/env bash
set -euo pipefail

usage() { echo "usage: $0 MANIFEST SIGNATURE PUBLIC_KEY" >&2; exit 2; }
[ "$#" -eq 3 ] || usage
manifest=$1
signature=$2
public_key=$3

[ -f "$manifest" ] || { echo "manifest not found: $manifest" >&2; exit 2; }
[ -f "$signature" ] || { echo "signature not found: $signature" >&2; exit 2; }
[ -f "$public_key" ] || { echo "public key not found: $public_key" >&2; exit 2; }

if ! openssl dgst -sha256 -verify "$public_key" -signature "$signature" "$manifest" >/dev/null 2>&1; then
  echo "release manifest signature verification failed" >&2
  exit 1
fi

echo "release manifest signature verified"
