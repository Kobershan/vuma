#!/usr/bin/env bash
#
# The full suite the way CI runs it: unit, architecture and integration tests, with a
# throwaway PostgreSQL for the integration ones. Arguments are passed through to dotnet test.
#
#   scripts/test.sh
#   scripts/test.sh --filter FullyQualifiedName~AuditInterceptor
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${REPO_ROOT}"

if [[ -z "${VUMA_TEST_POSTGRES:-}" ]]; then
  eval "$("${REPO_ROOT}/scripts/pg-test.sh" start)"
  trap '"${REPO_ROOT}/scripts/pg-test.sh" stop >/dev/null 2>&1 || true' EXIT
fi

export VUMA_TEST_POSTGRES

dotnet test -c Release "$@"
