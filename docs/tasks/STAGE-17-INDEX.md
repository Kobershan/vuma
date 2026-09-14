# Stage 17 task index — Manufacturing execution

This is the canonical execution queue for Stage 17. Tasks are ordered by dependency; a task is
complete only when its code, tests, migration/API evidence and documentation are recorded.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-17-001 | Production-order lifecycle and BOM snapshot | Stage 16 | COMPLETE — source build and PostgreSQL migration evidence pass |
| TASK-17-002 | Material issue, output receipt, scrap, stock and financial integration | TASK-17-001 | COMPLETE — PostgreSQL stock/financial reconciliation evidence passes |
| TASK-17-003 | Capacity/genealogy queries, API, replay and closure evidence | TASK-17-002 | COMPLETE — CI API/replay, migration, security and architecture gates pass |

## Verification record (2026-09-14)

- Manufacturing API integration: **7/7 passed** against PostgreSQL, including company-bound BOM
  reads/publication, production release, material issue replay, shortage rollback, output, scrap,
  close, capacity, and genealogy.
- Manufacturing migration verification: **2/2 passed** against PostgreSQL.
- The API now accepts the selected `companyId` on BOM reads/publication and production mutations that
  need to rebind the per-request active-company context; missing context remains fail-closed.
## Closure decision (2026-09-14)

The implementation, changed-payload replay regression, full CI test suite, PostgreSQL migration checks,
API, authorization, offline replay, genealogy, capacity, seed/backup and architecture gates pass in
GitHub Actions run 34824716619. The external specialist-agent runtime is unavailable in this
environment and is recorded as such; no specialist review is claimed.
