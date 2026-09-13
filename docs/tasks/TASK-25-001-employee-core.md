# TASK-25-001 — Employee core

Creates tenant-scoped employees with normalized identifiers, employment type and lifecycle state.

Acceptance evidence: `Employee.Create` rejects missing tenant/type, normalizes the employee number,
and termination enforces a non-preceding timestamp. Verified by `HrLifecycleTests` on 2026-09-13.
