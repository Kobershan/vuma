# Stage 18 task index — Quality management

This is the canonical execution queue for Stage 18. A task is complete only when its code,
tests, migration/API evidence and documentation are recorded.

> **Audit correction (2026-09-14):** The earlier closure note overstated completion. The warehouse
> dispatch quality gate is now implemented, but source-built regression evidence, PostgreSQL
> shelf-life/dispatch evidence, and automatic recall traceability are still open.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-18-001 | Inspection plans/results and immutable evidence | Stage 12, 17 | COMPLETE — versioning, immutable results, replay, scope and migration evidence pass |
| TASK-18-002 | Quality holds, dispositions and stock availability | TASK-18-001, Stage 08c | IN_PROGRESS — reservation-backed availability and PostgreSQL expiry dispatch pass; reservation projection traceability remains |
| TASK-18-003 | NCR/CAPA, certificates, recalls, shelf life and closure | TASK-18-002, Stage 24 | IN_PROGRESS — lifecycle/API evidence passes; automatic lot-to-output/shipment recall traceability remains |

## Verification record (2026-09-14)

- Quality API integration: **4/4 passed** against PostgreSQL, including OpenAPI coverage,
  permission denial, company-scoped quality setup, reservation-backed hold idempotency, and atomic
  shortfall refusal.
## Closure decision (2026-09-14)

Stage 18 remains in progress. The quality unit suite is recorded at 17/17, focused quality
integration evidence at 5/5, quality API evidence at 4/4, and migration reversibility at 1/1
against PostgreSQL. The warehouse chain now proves expiry refusal; application recall traversal covers
input lot → production order → output lot → shipment, while PostgreSQL genealogy and reservation
projection evidence remain open.
