# Stage 17 task index — Manufacturing execution

This is the canonical execution queue for Stage 17. Tasks are ordered by dependency; a task is
complete only when its code, tests, migration/API evidence and documentation are recorded.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-17-001 | Production-order lifecycle and BOM snapshot | Stage 16 | IN PROGRESS — PostgreSQL/API evidence exists; broader scope closure remains |
| TASK-17-002 | Material issue, output receipt, scrap, stock and financial integration | TASK-17-001 | IN PROGRESS — PostgreSQL stock shortage, replay and scrap-journal evidence now passes; closure is gated by the stage queue |
| TASK-17-003 | Capacity/genealogy queries, API, replay and closure evidence | TASK-17-002 | IN PROGRESS — API, genealogy, capacity, seed and backup evidence pass; specialist review remains UNVERIFIED |
