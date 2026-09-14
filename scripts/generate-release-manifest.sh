#!/usr/bin/env bash
set -euo pipefail

usage() { echo "usage: $0 RELEASE_ROOT MANIFEST" >&2; exit 2; }
[ "$#" -eq 2 ] || usage
root=$(realpath "$1")
manifest=$2
[ -d "$root" ] || { echo "release root not found: $root" >&2; exit 2; }
case "$manifest" in /*) ;; *) manifest=$(realpath -m "$manifest") ;; esac
manifest=$(realpath -m "$manifest")
mkdir -p "$(dirname "$manifest")"

tmp=$(mktemp "${manifest}.tmp.XXXXXX")
trap 'rm -f "$tmp"' EXIT

while IFS= read -r -d '' file; do
  case "$file" in
    "$manifest"|"$manifest".tmp.*) continue ;;
  esac
  relative=${file#"$root/"}
  case "/$relative/" in
    */.git/*|*/.git|*/bin/*|*/obj/*|*/.gradle/*|*/build/*|*/node_modules/*) continue ;;
  esac
  case "$relative" in
    *.pem|*.key|*.p12|*.pfx|*.env|appsettings*.json) continue ;;
  esac
  printf '%s  %s\n' "$(sha256sum "$file" | awk '{print $1}')" "$relative"
done < <(find -P "$root" -type f -print0 | LC_ALL=C sort -z) > "$tmp"

[ -s "$tmp" ] || { echo "release contains no eligible payload files" >&2; exit 1; }
mv -f "$tmp" "$manifest"
trap - EXIT
echo "release manifest generated: $manifest"
