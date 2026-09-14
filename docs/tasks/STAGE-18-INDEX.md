# Stage 18 task index — Quality management

This is the canonical execution queue for Stage 18. A task is complete only when its code,
tests, migration/API evidence and documentation are recorded.

> **Audit correction (2026-09-14):** The earlier closure note overstated completion. The warehouse
> dispatch quality gate and automatic recall traceability are implemented; reservation projection
> traceability and specialist closure evidence remain open.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-18-001 | Inspection plans/results and immutable evidence | Stage 12, 17 | COMPLETE — versioning, immutable results, replay, scope and migration evidence pass |
| TASK-18-002 | Quality holds, dispositions and stock availability | TASK-18-001, Stage 08c | COMPLETE — reservation-backed availability, expiry dispatch and tracked reservation projection pass |
| TASK-18-003 | NCR/CAPA, certificates, recalls, shelf life and closure | TASK-18-002, Stage 24 | IN_PROGRESS — lifecycle/API evidence passes; automatic lot-to-output/shipment recall traceability remains |

## Verification record (2026-09-14)

- Quality API integration: **5/5 passed** against PostgreSQL, including OpenAPI coverage,
  permission denial, company-scoped quality setup, reservation-backed hold idempotency, and atomic
  shortfall refusal; the tracked quality-hold reservation projection scenario is also green (**6/6**).
## Closure decision (2026-09-14)

Stage 18 remains in progress. The quality unit suite is recorded at 17/17, focused quality
integration evidence at 5/5, quality API evidence at 6/6, and migration reversibility at 1/1
against PostgreSQL. The warehouse chain now proves expiry refusal; application recall traversal covers
input lot → production order → output lot → shipment, and tracked quality holds preserve their batch
identity in the company reservation projection. TASK-18-002 is complete; TASK-18-003 remains open
for specialist closure evidence.
