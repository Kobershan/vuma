#!/usr/bin/env bash
set -euo pipefail

usage() { echo "usage: $0 RELEASE_DIR ACTIVE_LINK ROLLBACK_LINK [--rollback]" >&2; exit 2; }
[ "$#" -eq 3 ] || [ "$#" -eq 4 ] || usage
release_dir=$1
active_link=$2
rollback_link=$3
mode=${4:-}

if [ "$mode" = "--rollback" ]; then
  [ -L "$rollback_link" ] || { echo "rollback target is not available: $rollback_link" >&2; exit 1; }
  target=$(readlink "$rollback_link")
else
  [ -d "$release_dir" ] || { echo "release directory not found: $release_dir" >&2; exit 1; }
  target=$(realpath "$release_dir")
fi

parent=$(dirname "$active_link")
mkdir -p "$parent"
tmp_active="$active_link.tmp.$$"
tmp_rollback="$rollback_link.tmp.$$"
cleanup() { rm -f "$tmp_active" "$tmp_rollback"; }
trap cleanup EXIT

# Single-filesystem renames make each pointer switch atomic. Preserve the old active target first.
if [ -L "$active_link" ]; then
  ln -s "$(readlink "$active_link")" "$tmp_rollback"
  mv -Tf "$tmp_rollback" "$rollback_link"
elif [ -e "$active_link" ]; then
  echo "active path exists but is not a symlink: $active_link" >&2
  exit 1
fi

ln -s "$target" "$tmp_active"
mv -Tf "$tmp_active" "$active_link"
echo "active release: $(readlink "$active_link")"
