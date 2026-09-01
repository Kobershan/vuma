# TASK-012 — Migration fan-out and serving guard

## Objective
Migrate registry first and fan out the company migration chain with truthful per-company progress and serving refusal.
## Why
Partial upgrades are inevitable; serving a company against an unexpected schema is not acceptable.
## Scope
Migration runner, bounded parallelism, `schema_version`/`migration_state`, progress reporting, retry/resume, version serving guard.
## Out of scope
Business-table retrofit, provisioning seed content, backup restore, and API administration beyond migration reporting.
## Affected files/components
Infrastructure migration services; registry company state; application serving guard; migration integration tests.
## Dependencies
TASK-005, TASK-007 and TASK-010; ADR-117.
## Relevant references
Stage 06c migration deliverables; `docs/MULTI_COMPANY.md` §9; `docs/DATA_MODEL.md` §4l; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
Migrate registry first; bound company concurrency; record failures and pending migration name; resume safely; refuse a company behind the binary with an actionable named response; exercise `Down` per company.
## Acceptance criteria
One unreachable company does not prevent others migrating; the unreachable company remains pending and is refused by serving guard; rerun completes it; up/down is verified on real databases.
## Tests
Runner ordering and concurrency tests; outage integration test; guard response test; resumability test; per-company up/down migration test.
## Edge cases
Registry unavailable, company disappears, migration timeout, mixed versions, cancellation after one success, failed down migration.
## Security considerations
`platform.migrate` is restricted; migration diagnostics redact connection data; do not execute arbitrary client-selected migrations.
## Architectural decision impact
Direct implementation of ADR-117; preserve ADR-099 separate histories.
## Definition of done
Runner, guard, tests, progress evidence and architecture review are complete.
## Status
COMPLETE
## Work log
- 2026-08-27: Planned after provisioning and routing state exist.
- 2026-08-27: Added registry-first migration runner with bounded company fan-out, per-company progress, and serving guard refusal for pending migrations.
