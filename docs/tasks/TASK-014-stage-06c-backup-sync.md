# TASK-014 — Per-database backup and sync boundaries

## Objective
Define and implement per-company plus registry snapshot, restore, outbox/inbox and cursor boundaries.
## Why
One company must be restorable or offline without stopping siblings, and sync must not merge databases accidentally.
## Scope
Snapshot scheduling/retention/encryption metadata; per-database restore seam; registry re-drive trigger; company sync registration, cursors, outbox/inbox and cloud layout.
## Out of scope
Full DR drill (Stage 31), new sync conflict policy, saga business handlers, and cloud infrastructure redesign.
## Affected files/components
Backup and sync services/contracts; registry snapshot ledger; company context registration; integration tests and docs references.
## Dependencies
TASK-006, TASK-008 and TASK-013; ADR-120; existing Stage 04 backup/sync seams.
## Relevant references
Stage 06c backup/sync deliverable; `docs/MULTI_COMPANY.md` §9; `docs/DATA_MODEL.md` §§2b–3; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
Track database identity in every snapshot; give registry its own cadence; restore one company independently; re-drive eligible legs after restore; keep cursors/outboxes isolated per company and mirror them in cloud.
## Acceptance criteria
Backup/restore tests prove sibling availability; snapshot ledger records the set; restored company is re-driven safely; no cursor/outbox is shared across companies.
## Tests
Snapshot metadata and retention tests; isolated restore integration test; re-drive idempotency test; sync registration/cursor isolation tests.
## Edge cases
Registry restore, company restore to older version, missing snapshot, duplicate re-drive, company outage, retention boundary.
## Security considerations
Encrypt snapshots, restrict restore, redact connection details, and never include credentials in telemetry/support exports.
## Architectural decision impact
Direct implementation of ADR-120 and reinforcement of ADR-116 idempotent legs.
## Definition of done
Operational seams, tests, docs evidence and sync specialist review are complete.
## Status
NOT_STARTED
## Work log
- 2026-08-27: Planned after company identity and saga records are stable.

