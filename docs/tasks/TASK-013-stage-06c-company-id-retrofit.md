# TASK-013 — company_id retrofit across existing business tables

## Objective
Add and backfill `company_id` on every existing business row while preserving one-company behavior.
## Why
The database wall is primary, but self-describing exports/restores and registry projections require the redundant key.
## Scope
Existing business entities/configurations/migrations; non-null backfill to the migrated single company; indexes and invariants; regression tests.
## Out of scope
New registry tables, cross-company features, data redesign, and unrelated defect repair.
## Affected files/components
Domain entities/configurations; company migration chain; repositories/query filters; existing unit/integration tests.
## Dependencies
TASK-008 and TASK-012; `docs/DATA_MODEL.md` §§2b–4; ADR-099 and ADR-119.
## Relevant references
Stage 06c retrofit deliverable; `docs/MULTI_COMPANY.md` §2; `CLAUDE.md` §7.
## Implementation requirements
Inventory all business tables, add non-null company identity with deterministic backfill, maintain tenant/company consistency, preserve existing single-company queries and sync metadata, and make migration reversible where safe.
## Acceptance criteria
Every existing business row has the correct company ID; existing tests remain green; no cross-company foreign key is introduced; restored/exported rows identify their company.
## Tests
Model inventory/architecture test; backfill integration test; query isolation tests; migration up/down test; sync serialization regression tests.
## Edge cases
Empty tables, shared platform rows, null legacy data, duplicate IDs, historical rows with no store, rollback after partial backfill.
## Security considerations
Company ID is not authorization by itself; enforce tenant and access checks; prevent client-controlled reassignment.
## Architectural decision impact
Implements ADR-099's redundant identity and ADR-119 projection key; architecture-guard review required.
## Definition of done
All scoped tables migrated/backfilled, tests and review green, exceptions documented as follow-up.
## Status
IN_PROGRESS
## Work log
- 2026-08-27: Planned as a bounded retrofit inventory rather than a broad refactor.
- 2026-08-28: Added shared `Entity.CompanyId`, base EF mapping/index, reversible schema retrofit migration, and migration-runner backfill path. Real database inventory/backfill verification remains before completion.
