# TASK-010 — Provisioning lifecycle and resumability

## Objective
Implement company creation as a resumable `Provisioning → Seeding → Registered → Active` workflow.
## Why
A database can be created while a later step fails; the registry must never expose a half-ready company.
## Scope
Provision command/orchestrator; database creation; company migration; chart/numbering/tax/permission seed hooks; progress/error recording; resume.
## Out of scope
Public API, migration fleet runner, backup/sync, groups, and company deactivation policy beyond the lifecycle seam.
## Affected files/components
Application provisioning; infrastructure database/migration/seed adapters; registry records; integration tests and seed scripts.
## Dependencies
TASK-006 and TASK-008; ADR-118; Stage 01/04/06/07 seed conventions.
## Relevant references
Stage 06c deliverables/business rules; `docs/MULTI_COMPANY.md` §9; `docs/DATA_MODEL.md` §4l; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
Record each step and failure; resume from the failed step idempotently; register connection only after migration/seed; activate only after all checks; do not expose non-active companies to business operations.
## Acceptance criteria
Three empty companies provision with isolated databases, independent numbering and seeded accounts; killing after database creation and rerunning completes without an active interim state.
## Tests
Lifecycle/domain tests; failure injection per step; resume/idempotency integration tests; isolation and seed verification.
## Edge cases
Database already exists, duplicate retry, seed partially applied, connection registration failure, activation race, entitlement limit.
## Security considerations
Provisioning requires permission and entitlement; database credentials are secret references; errors reveal no credentials.
## Architectural decision impact
Implements ADR-118 and must preserve ADR-099/116 boundaries.
## Definition of done
Resumable workflow, tests, seed evidence, specialist review and green checkpoint are recorded.
## Status
COMPLETE
## Work log
- 2026-08-27: Planned after registry persistence and one-company context seams.
