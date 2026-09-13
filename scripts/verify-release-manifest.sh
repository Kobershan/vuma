#!/usr/bin/env bash
set -euo pipefail

usage() { echo "usage: $0 MANIFEST [ROOT]" >&2; exit 2; }
[ "$#" -ge 1 ] && [ "$#" -le 2 ] || usage
manifest=$1
root=${2:-.}
[ -f "$manifest" ] || { echo "manifest not found: $manifest" >&2; exit 2; }
root=$(realpath "$root")
[ -d "$root" ] || { echo "release root not found: $root" >&2; exit 2; }

failed=0
entries=0
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in ''|'#'*) continue ;; esac
  entries=$((entries + 1))
  hash=${line%%  *}
  relative=${line#*  }
  if [ "$hash" = "$line" ] || [ -z "$relative" ] || [[ ! "$hash" =~ ^[[:xdigit:]]{64}$ ]] ||
    [[ "$relative" = /* || "$relative" = .. || "$relative" = ../* || "$relative" = */../* || "$relative" = */.. ]]; then
    echo "invalid manifest row: $line" >&2
    failed=1
    continue
  fi
  file="$root/$relative"
  if [ ! -f "$file" ] || [ -L "$file" ]; then
    echo "missing: $relative" >&2
    failed=1
    continue
  fi
  actual=$(sha256sum "$file" | awk '{print $1}')
  if [ "${actual,,}" != "${hash,,}" ]; then
    echo "checksum mismatch: $relative" >&2
    failed=1
  else
    echo "verified: $relative"
  fi
done < "$manifest"

if [ "$entries" -eq 0 ]; then
  echo "release manifest is empty" >&2
  failed=1
fi

if [ "$failed" -ne 0 ]; then
  echo "release manifest verification failed" >&2
  exit 1
fi
echo "release manifest verified"
