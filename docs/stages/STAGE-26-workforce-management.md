# Stage 26 — Workforce Management

**Status:** IN_PROGRESS — shift overlap protection, immutable attendance foundations, persistence, permissions, roster publication and API routes are implemented; labour-cost integration and full acceptance remain.

Owns workforce shifts and immutable attendance events in `hr_workforce`. Scheduling uses employee IDs,
store IDs and application-level availability checks so HR and workforce remain independently extractable.

Completed: TASK-26-001 shifts, TASK-26-002 attendance.

Verification recorded 2026-09-13: `HrStagesRulesTests` (6/6) and `HrLifecycleTests` (6/6) pass. Shift creation refuses an overlapping non-cancelled shift for the same employee, and `GET /api/v1/workforce/employees/{employeeId}/availability` reports lifecycle- and schedule-aware availability. Roster publication, swaps and labour-cost integration remain open.

2026-09-13: Added the `ShiftSwapRequest` approval state machine, tenant-scoped EF persistence,
request/decision commands, workforce routes, same-employee/decide-once invariants, and atomic
approved transfer with target-employee overlap and owner-change checks. The focused HR lifecycle
suite passes 11/11. Labour-cost integration remains open.

2026-09-13: Added company-guarded roster publication with deterministic SHA-256 snapshots and
tenant-scoped persistence in migration `Stage26RosterPublication`; the HR lifecycle suite passes
11/11. Labour-cost integration and specialist review remain open.

2026-09-14: Employee/shift creation and availability now validate the loaded employee tenant
before creating or returning workforce data. HR-focused tests pass **102/102**.
