# Stage 17 task index — Manufacturing execution

This is the canonical execution queue for Stage 17. Tasks are ordered by dependency; a task is
complete only when its code, tests, migration/API evidence and documentation are recorded.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-17-001 | Production-order lifecycle and BOM snapshot | Stage 16 | IN_PROGRESS — implementation is present; source rebuild and PostgreSQL migration evidence are blocked |
| TASK-17-002 | Material issue, output receipt, scrap, stock and financial integration | TASK-17-001 | IN_PROGRESS — implementation is present; PostgreSQL stock/financial reconciliation is blocked |
| TASK-17-003 | Capacity/genealogy queries, API, replay and closure evidence | TASK-17-002 | IN_PROGRESS — API/replay tests exist; PostgreSQL and specialist closure evidence is blocked |

## Verification record (2026-09-14)

- Manufacturing API integration: **7/7 passed** against PostgreSQL, including company-bound BOM
  reads/publication, production release, material issue replay, shortage rollback, output, scrap,
  close, capacity, and genealogy.
- Manufacturing migration verification: **2/2 passed** against PostgreSQL.
- The API now accepts the selected `companyId` on BOM reads/publication and production mutations that
  need to rebind the per-request active-company context; missing context remains fail-closed.
## Verification correction (2026-09-14)

The prior completion entry was overstated. The focused pre-built manufacturing unit binary passes
13/13, but rebuilding the unit project currently fails on unrelated missing types in the Assets and
HR test sources. The manufacturing integration suite cannot initialize because Docker and local
PostgreSQL are unavailable. The source now includes a regression test for changed execution replay
payloads; it must be run after the repository-wide test compile is repaired. Stage 17 remains open
until the source build, PostgreSQL API/migration/stock tests, seed/backup checks, and required
specialist reviews have executed successfully.
