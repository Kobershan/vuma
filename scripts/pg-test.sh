#!/usr/bin/env bash
#
# A throwaway PostgreSQL cluster for the integration tests, using the locally installed
# server binaries. No Docker, no sudo, no shared state with a system cluster.
#
# Testcontainers is the documented path (docs/TESTING.md §2) and the fixture prefers it when
# Docker is available. This exists for machines where it is not — which, per
# docs/PROGRESS.md §4.3, is the machine Zenith is currently being built on. See ADR-036.
#
#   scripts/pg-test.sh start     start the cluster and print the export line
#   scripts/pg-test.sh stop      stop it and delete the data directory
#   scripts/pg-test.sh status    is it up?
#   eval "$(scripts/pg-test.sh start)"   start it and set ZENITH_TEST_POSTGRES in this shell
#
set -euo pipefail

PORT="${ZENITH_TEST_PG_PORT:-55432}"
DATA_DIR="${ZENITH_TEST_PG_DATA:-${TMPDIR:-/tmp}/zenith-test-pg}"
USER_NAME="zenith"

find_pg_bin() {
  # Debian and Ubuntu keep the server binaries off PATH, one directory per major version.
  if command -v initdb >/dev/null 2>&1; then
    dirname "$(command -v initdb)"
    return
  fi

  local candidate
  candidate="$(ls -d /usr/lib/postgresql/*/bin 2>/dev/null | sort -V | tail -1 || true)"

  if [[ -n "${candidate}" && -x "${candidate}/initdb" ]]; then
    echo "${candidate}"
    return
  fi

  echo "PostgreSQL server binaries not found. Install postgresql (not just the client)." >&2
  exit 1
}

PG_BIN="$(find_pg_bin)"

conn_string() {
  echo "Host=127.0.0.1;Port=${PORT};Database=postgres;Username=${USER_NAME};Password=zenith"
}

case "${1:-start}" in
  start)
    if "${PG_BIN}/pg_isready" -h 127.0.0.1 -p "${PORT}" >/dev/null 2>&1; then
      echo "export ZENITH_TEST_POSTGRES='$(conn_string)'"
      exit 0
    fi

    rm -rf "${DATA_DIR}"

    # trust auth: the cluster listens on loopback only, on a non-standard port, and is deleted
    # after the run. There is no credential here worth protecting and none is ever committed.
    "${PG_BIN}/initdb" -D "${DATA_DIR}" -U "${USER_NAME}" --auth=trust -E UTF8 >/dev/null

    "${PG_BIN}/pg_ctl" -D "${DATA_DIR}" -l "${DATA_DIR}/server.log" \
      -o "-p ${PORT} -k ${DATA_DIR} -c listen_addresses=127.0.0.1 -c fsync=off -c full_page_writes=off" \
      start >/dev/null

    # fsync off is safe and much faster here: the entire cluster is disposable, so durability
    # across a crash buys nothing and costs a few seconds on every migration run.

    for _ in $(seq 1 30); do
      "${PG_BIN}/pg_isready" -h 127.0.0.1 -p "${PORT}" >/dev/null 2>&1 && break
      sleep 0.5
    done

    echo "export ZENITH_TEST_POSTGRES='$(conn_string)'"
    ;;

  stop)
    if [[ -d "${DATA_DIR}" ]]; then
      "${PG_BIN}/pg_ctl" -D "${DATA_DIR}" -m immediate stop >/dev/null 2>&1 || true
      rm -rf "${DATA_DIR}"
    fi
    echo "stopped" >&2
    ;;

  status)
    "${PG_BIN}/pg_isready" -h 127.0.0.1 -p "${PORT}"
    ;;

  *)
    echo "usage: $0 {start|stop|status}" >&2
    exit 64
    ;;
esac
