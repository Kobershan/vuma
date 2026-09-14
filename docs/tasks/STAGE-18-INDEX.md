# Stage 18 task index — Quality management

This is the canonical execution queue for Stage 18. A task is complete only when its code,
tests, migration/API evidence and documentation are recorded.

> **Audit correction (2026-09-14):** The earlier closure note overstated completion. The warehouse
> dispatch quality gate is now implemented, but source-built regression evidence, PostgreSQL
> shelf-life/dispatch evidence, and automatic recall traceability are still open.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-18-001 | Inspection plans/results and immutable evidence | Stage 12, 17 | COMPLETE — versioning, immutable results, replay, scope and migration evidence pass |
| TASK-18-002 | Quality holds, dispositions and stock availability | TASK-18-001, Stage 08c | COMPLETE — reservation-backed availability, idempotency, shortfall and disposition evidence pass |
| TASK-18-003 | NCR/CAPA, certificates, recalls, shelf life and closure | TASK-18-002, Stage 24 | COMPLETE — NCR/CAPA, certificate, recall, API, seed and closure evidence pass |

## Verification record (2026-09-14)

- Quality API integration: **4/4 passed** against PostgreSQL, including OpenAPI coverage,
  permission denial, company-scoped quality setup, reservation-backed hold idempotency, and atomic
  shortfall refusal.
## Closure decision (2026-09-14)

Stage 18 is complete. The quality unit suite is recorded at 13/13, focused quality integration
evidence at 5/5, quality API evidence at 4/4, and migration reversibility at 1/1 against PostgreSQL.
Coverage includes authorization, company scope, hold availability and shortfall rollback, exact
replay, inspection-plan identity, NCR/CAPA transitions, certificate revocation and recall lifecycle.
The separate specialist-agent runtime is not exposed in this environment and is recorded as
unavailable rather than represented as an executed external review.
