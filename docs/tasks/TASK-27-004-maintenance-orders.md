# TASK-27-004 — Maintenance order lifecycle

Adds a company-scoped maintenance order tied to a fixed asset. Orders move from Open to InProgress
to Completed, or may be cancelled before completion; closed orders cannot be reopened or cancelled.

Verification: `MaintenanceOrderTests` (2/2) passes on 2026-09-13. Persistence, parts/labour capture,
finance posting and offline checklist integration remain assigned to Stage 27. The four maintenance
routes are present in the real OpenAPI document; `The_openapi_document_is_served_and_describes_every_endpoint`
passes 1/1 against PostgreSQL on 2026-09-13. API authorization still requires a dedicated acceptance
scenario.
