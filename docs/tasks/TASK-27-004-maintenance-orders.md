# TASK-27-004 — Maintenance order lifecycle

Adds a company-scoped maintenance order tied to a fixed asset. Orders move from Open to InProgress
to Completed, or may be cancelled before completion; closed orders cannot be reopened or cancelled.

Verification: `MaintenanceOrderTests` (2/2) passes on 2026-09-13. Persistence, parts/labour capture,
finance posting, API authorization and offline checklist integration remain assigned to Stage 27.
