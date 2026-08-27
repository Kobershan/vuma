# TASK-006 — Registry groups, saga intents, legs and outbox

## Objective
Create registry persistence for company groups, immutable saga intents, idempotent legs, and registry outbox dispatch records.
## Why
Cross-company work must have an auditable coordination record without a distributed transaction.
## Scope
Registry group/member records; intent/leg state, keys, attempts, acknowledgements and timeout ownership; registry outbox mapping and migration.
## Out of scope
Saga execution policy, credit groups, routing projections, business commands, cross-company links, and company database writes.
## Affected files/components
Registry domain records; registry EF configurations/context; migrations; outbox/registry unit and integration tests.
## Dependencies
TASK-005; ADR-099, ADR-116 and `docs/MULTI_COMPANY.md` §§1–2, 9.
## Relevant references
Stage 06c; `docs/DATA_MODEL.md` §4l; `docs/EXECUTION_STANDARD.md`; sync/outbox conventions from Stage 04.
## Implementation requirements
Make intents immutable; key each leg by `(intent_id, leg_id)`; constrain company ownership to registry identifiers; represent retryable states explicitly; include timeout/owner fields and standard audit/sync metadata where applicable.
## Acceptance criteria
A complete intent and multiple legs persist atomically in the registry; duplicate leg insertion is rejected/idempotently resolved; outbox rows are durable and migration is reversible.
## Tests
Aggregate invariant tests; uniqueness/constraint tests; persistence round trip; duplicate/retry state tests; migration up/down.
## Edge cases
Zero legs, duplicate leg IDs, late acknowledgement, timeout without acknowledgement, compensated intent, group membership duplication.
## Security considerations
Payloads must exclude secrets; authorize registry writes; audit state transitions and avoid leaking cross-company data in logs.
## Architectural decision impact
Directly enforces ADR-116; groups are containers only and do not introduce a cross-database transaction.
## Definition of done
Model, migration, tests, review evidence and handoff are complete at a green checkpoint.
## Status
COMPLETE
## Work log
- 2026-08-27: Planned as the registry coordination persistence slice after TASK-005.
