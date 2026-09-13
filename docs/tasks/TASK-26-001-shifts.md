# TASK-26-001 — Workforce shifts

Adds scheduled employee shifts, cancellation/completion lifecycle and store scoping. Employee identity
is an ID-only integration reference; no cross-module foreign key is created.

The application command now checks the employee's existing window and refuses overlap with any
non-cancelled shift. The boundary is covered by `Shift.Overlaps` in `HrLifecycleTests`; transactional
database uniqueness/concurrency and roster swaps remain open.
