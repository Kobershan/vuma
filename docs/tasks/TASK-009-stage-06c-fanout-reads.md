# TASK-009 — Fan-out reads with per-company failures

## Objective
Provide bounded-parallelism reads across selected active companies with a result for every company.
## Why
Group-facing reads must remain useful when one company database is unavailable.
## Scope
`ICompanyFanOut`, bounded concurrency, cancellation/timeouts, per-company success/error result contract, and observability.
## Out of scope
Registry projections, writes/sagas, sourcing decisions, API presentation, and cross-company transactions.
## Affected files/components
Application fan-out port/service; infrastructure execution; contracts if required; unit/integration tests.
## Dependencies
TASK-008; ADR-119; `docs/MULTI_COMPANY.md` §§2–4.
## Relevant references
Stage 06c; `docs/DATA_MODEL.md` §2b; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
Bound concurrency; isolate each company failure; preserve company ID and `AsAt`/staleness metadata; honor cancellation and avoid opening one transaction across companies.
## Acceptance criteria
Three-company read with one stopped returns two values and one named error, not a 500; slow/failing companies do not exhaust resources or hide successful results.
## Tests
Unit result-shape tests; integration outage test; concurrency-bound test; cancellation and timeout tests.
## Edge cases
All companies down, duplicate IDs, deactivated company between selection and read, partial timeout, empty selection.
## Security considerations
Authorize the selected company set and redact connection/database errors from external responses.
## Architectural decision impact
Implements ADR-119's degraded-read rule and must not become a commit path.
## Definition of done
Service, contract, tests and specialist evidence are complete and green.
## Status
COMPLETE
## Work log
- 2026-08-27: Planned as a read-only seam after company context enforcement.
