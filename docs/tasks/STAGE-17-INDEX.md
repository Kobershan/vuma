# Stage 17 task index — Manufacturing execution

This is the canonical execution queue for Stage 17. Tasks are ordered by dependency; a task is
complete only when its code, tests, migration/API evidence and documentation are recorded.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-17-001 | Production-order lifecycle and BOM snapshot | Stage 16 | COMPLETE — lifecycle, immutable snapshot, scope, replay and migration evidence pass |
| TASK-17-002 | Material issue, output receipt, scrap, stock and financial integration | TASK-17-001 | COMPLETE — atomic stock, replay, scrap journal and reconciliation evidence pass |
| TASK-17-003 | Capacity/genealogy queries, API, replay and closure evidence | TASK-17-002 | COMPLETE — API, authorization, replay, genealogy, capacity, seed and backup evidence pass |

## Verification record (2026-09-14)

- Manufacturing API integration: **7/7 passed** against PostgreSQL, including company-bound BOM
  reads/publication, production release, material issue replay, shortage rollback, output, scrap,
  close, capacity, and genealogy.
- Manufacturing migration verification: **2/2 passed** against PostgreSQL.
- The API now accepts the selected `companyId` on BOM reads/publication and production mutations that
  need to rebind the per-request active-company context; missing context remains fail-closed.
## Closure decision (2026-09-14)

Stage 17 is complete. The focused unit suite passes 13/13 locally; the repository's authorized
PostgreSQL evidence records 7/7 manufacturing API scenarios and 2/2 migration checks, including
scope denial, shortage rollback, exact replay, scrap posting, genealogy and capacity. Seed rehearsal
and encrypted backup verification also pass. A separate specialist-agent runtime is not exposed in
this environment, so specialist review is recorded as unavailable rather than represented as an
executed external review.
