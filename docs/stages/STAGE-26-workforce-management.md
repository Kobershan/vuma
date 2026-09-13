# Stage 26 — Workforce Management

**Status:** IN_PROGRESS — shift overlap protection, immutable attendance foundations, persistence, permissions and API routes are implemented; roster/availability, swaps, labour-cost integration and full acceptance remain.

Owns workforce shifts and immutable attendance events in `hr_workforce`. Scheduling uses employee IDs,
store IDs and application-level availability checks so HR and workforce remain independently extractable.

Completed: TASK-26-001 shifts, TASK-26-002 attendance.

Verification recorded 2026-09-13: `HrStagesRulesTests` (6/6) and `HrLifecycleTests` (6/6) pass. Shift creation refuses an overlapping non-cancelled shift for the same employee, and `GET /api/v1/workforce/employees/{employeeId}/availability` reports lifecycle- and schedule-aware availability. Roster publication, swaps and labour-cost integration remain open.
