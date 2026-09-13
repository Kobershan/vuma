# Stage 26 — Workforce Management

**Status:** IN_PROGRESS — shift and immutable attendance foundations, persistence, permissions and API routes are implemented; roster/availability, swaps, labour-cost integration and full acceptance remain.

Owns workforce shifts and immutable attendance events in `hr_workforce`. Scheduling uses employee IDs,
store IDs and application-level availability checks so HR and workforce remain independently extractable.

Completed: TASK-26-001 shifts, TASK-26-002 attendance.

Verification recorded 2026-09-13: `HrStagesRulesTests` (6/6) and `HrLifecycleTests` (4/4) pass. Remaining workforce integration scope is intentionally open pending its executable acceptance evidence.
