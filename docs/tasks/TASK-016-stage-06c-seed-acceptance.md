# TASK-016 — Seed data and acceptance verification

## Objective
Seed three isolated companies and prove the Stage 06c acceptance scenarios end to end.
## Why
The stage is only real when independent databases, numbering, accounts, lifecycle and failure behavior are demonstrable.
## Scope
Three-company seed path; single-company upgrade seed; numbering/chart/tax/permission verification; stage acceptance tests and coverage measurement.
## Out of scope
New product features, later group services, Stage 31 DR drill, and unrelated stage defect closure.
## Affected files/components
Seed scripts/fixtures; registry and company databases; Stage 06c unit/integration/architecture tests; task/stage evidence.
## Dependencies
TASK-010 through TASK-015 as listed in the index.
## Relevant references
Stage 06c Tests/acceptance and Exit checklist; `docs/MULTI_COMPANY.md`; `docs/DATA_MODEL.md`; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
Provision hardware, DC and groceries with separate databases and numbering starting at 1; verify seeded chart of accounts; run fan-out outage, migration refusal, restore and architecture scenarios; measure Domain + Application coverage against 80%.
## Acceptance criteria
All Stage 06c acceptance cases are executable with evidence; existing suite remains green; no source outside authorized scope is changed.
## Tests
Full applicable suite; real PostgreSQL migration/restore/fan-out tests; architecture test; coverage report; seed smoke test.
## Edge cases
Rerun seed, partial company, unreachable sibling, stale registry, duplicate tenant code, empty company.
## Security considerations
Use test credentials only; verify seed output contains no secrets; ensure test fixtures cannot escape tenant/company scope.
## Architectural decision impact
Validates ADR-099 and ADR-116–120 together; findings must be fixed or recorded before completion.
## Definition of done
Acceptance evidence, coverage, green builds, and known limitations are documented.
## Status
NOT_STARTED
## Work log
- 2026-08-27: Planned as the integrated acceptance checkpoint.

