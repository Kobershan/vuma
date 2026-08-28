# TASK-06D-001 — Implement saga coordination

## Status
NOT_STARTED
## Stage
Stage 06d
## Objective
Implement immutable intents, idempotent company legs, retries, reverse compensation, timeout alarms, and intent reporting.
## Why
Cross-company writes must be coordinated without distributed transactions.
## Scope
`ISagaCoordinator`, registry records, admin intents API, and tests.
## Out of Scope
Credit, routing, and projections beyond their saga integration.
## Dependencies
Stage 06c complete; TASK-06C-001.
## Relevant Files
`docs/stages/STAGE-06d-group-services.md`, `docs/MULTI_COMPANY.md` §3, saga infrastructure.
## References
ADR-116; Stage 06d Saga coordination section.
## Implementation Requirements
Legs are exactly-once by `(intent_id, leg_id)`, retry indefinitely, and compensate with new documents.
## Acceptance Criteria
Three failures then success applies once; timeout is an owned alarm.
## Tests Required
Saga replay, retry, compensation, timeout, and API tests.
## Edge Cases
Crash before acknowledgement and compensation failure.
## Security Considerations
Intent visibility is permissioned and audited.
## Architectural Impact
Shared write coordinator for 07c, 08c, and 14b.
## Definition of Done
Coordinator is reusable by later stages.
## Follow-up

# TASK-06D-002 — Implement group credit holds

## Status
NOT_STARTED
## Stage
Stage 06d
## Objective
Implement credit groups, serialisable holds, exposure, confirmation, release, expiry, and reports.
## Why
Credit spans companies but must remain correct under concurrency.
## Scope
Registry credit tables, `IGroupCreditService`, expiry job, and APIs.
## Out of Scope
Group receipting in Stage 07c.
## Dependencies
TASK-06D-001.
## Relevant Files
Stage 06d Credit groups; `docs/MULTI_COMPANY.md` §4/§6.
## References
ADR-101; Stage 06d acceptance.
## Implementation Requirements
Exposure equals confirmed consumption plus unexpired holds; holds are serialisable and idempotent.
## Acceptance Criteria
Two concurrent R4,000 demands against R5,000 permit exactly one; crash expiry restores exposure.
## Tests Required
Real registry concurrency, expiry, confirm/release replay, and refusal tests.
## Edge Cases
Sub-limits, expired holds, registry outage, and duplicate confirmation.
## Security Considerations
No cross-tenant exposure leakage.
## Architectural Impact
Group credit service consumed by future sales flows.
## Definition of Done
Credit position is correct through every accepted sequence.
## Follow-up

# TASK-06D-003 — Implement barcode routing and group read models

## Status
NOT_STARTED
## Stage
Stage 06d
## Objective
Build routing index, barcode resolver, availability/exposure/period projections, `AsAt`, stale disclosure, and rebuild paths.
## Why
Operators need one group view without treating projections as commit authority.
## Scope
Index publication, collision behavior, read stores, incremental/rebuild projection parity, and APIs.
## Out of Scope
Reservation and sourcing commitments in Stage 08c.
## Dependencies
TASK-06D-001, Stage 06c.
## Relevant Files
Stage 06d Catalogue routing and Group read models; `docs/MULTI_COMPANY.md` §11.
## References
ADR-100, ADR-119; Stage 06d acceptance.
## Implementation Requirements
Collisions return all candidates; every response carries `AsAt`; stale projections are disclosed and never authorize commits.
## Acceptance Criteria
500 randomized movements rebuild to the incremental projection; registry outage falls back to labelled local scan.
## Tests Required
Query-count, collision, stale projection, rebuild parity, and degraded-read tests.
## Edge Cases
Registry unavailable, retired barcode, missing contributor, and stale data.
## Security Considerations
Company/group authorization on every response.
## Architectural Impact
Shared read model consumed by 08c and later stages.
## Definition of Done
Routing and projection contracts are stable and tested.
## Follow-up

# TASK-06D-004 — Complete Stage 06d verification and seed

## Status
NOT_STARTED
## Stage
Stage 06d
## Objective
Run the stage guards, reversible registry migration, full suite, and required three-company seed.
## Why
Group services must be trusted before cross-company money or sourcing.
## Scope
Stage 06d exit checklist.
## Out of Scope
Later modules.
## Dependencies
TASK-06D-001 through TASK-06D-003.
## Relevant Files
Stage 06d Exit checklist; `docs/DATA_MODEL.md`, `docs/SYNC_AND_BACKUP.md`, `docs/PROGRESS.md`.
## References
`CLAUDE.md` §8; `docs/AGENTS.md`.
## Implementation Requirements
Close or record all guard findings and demonstrate shared customer, limit, collision, and stale projection seed.
## Acceptance Criteria
Every exit item has evidence.
## Tests Required
Full suite, migration Down, guards, and seed.
## Edge Cases
Regression failures in prior stages.
## Security Considerations
Review multi-company isolation.
## Architectural Impact
Closes Stage 06d.
## Definition of Done
Stage is ready for Stage 06e/07c.
## Follow-up

# TASK-06E-001 — Implement Operator ID and company links

## Status
NOT_STARTED
## Stage
Stage 06e
## Objective
Enforce vendor-issued Operator ID, link lifecycle, scopes, and cache invalidation.
## Why
Cross-company cooperation is permitted only by an active, scoped link.
## Scope
Registry aggregates, constraints, commands, APIs, and link service choke point.
## Out of Scope
Mixed-basket behavior and licence issuance.
## Dependencies
Stages 04b, 06c, 06d.
## Relevant Files
`docs/stages/STAGE-06e-trading-group.md`, `docs/TRADING_GROUP.md`, registry source.
## References
ADR-121–124, 127; Stage 06e Business rules.
## Implementation Requirements
Operator IDs must match; pairs are order-independent; scope is checked at operation time; links require both-side acceptance.
## Acceptance Criteria
Mismatched operator, inactive link, missing scope, suspension, and revocation all refuse with specified ProblemDetails.
## Tests Required
Aggregate, constraint, lifecycle, cache invalidation, API, and cross-company guard tests.
## Edge Cases
Licence fingerprint change and simultaneous acceptance/suspension.
## Security Considerations
Never mint Operator IDs in tenant code; enforce token company scope.
## Architectural Impact
Single authorization choke point for R13.
## Definition of Done
Every link operation is auditable and scope-checked.
## Follow-up

# TASK-06E-002 — Implement premises, shared bins, users, and tills

## Status
NOT_STARTED
## Stage
Stage 06e
## Objective
Add premises occupancy/layout mirroring, registry users, per-company access, and terminal company scope.
## Why
Shared physical operations require mastered premises and bounded identities.
## Scope
Registry entities, commands, APIs, saga mirror, JWT company claims, and metering counters.
## Out of Scope
Mixed basket till behavior and UI.
## Dependencies
TASK-06E-001.
## Relevant Files
Stage 06e Deliverables/API; `docs/TRADING_GROUP.md`; registry and token code.
## References
ADR-124, ADR-127; Stage 06e entitlement section.
## Implementation Requirements
Layout mirrors into occupying companies; JWT carries authorized companies/roles; entitlement limits block only new grants.
## Acceptance Criteria
Shared premises and terminal scopes are enforced and counters remain counts-only.
## Tests Required
Premises, mirror saga, token claims, terminal scope, entitlement, and API tests.
## Edge Cases
Occupancy dates, revoked access, and terminal with no companies.
## Security Considerations
Cross-company access must be explicit and auditable.
## Architectural Impact
Provides scopes consumed by 09b/13b/14b.
## Definition of Done
Registry administration is complete without adding UI.
## Follow-up

# TASK-06E-003 — Complete Stage 06e verification

## Status
NOT_STARTED
## Stage
Stage 06e
## Objective
Verify link enforcement, migrations, permissions, metering, and required seed data.
## Why
Later cross-company writes must have a proven authorization boundary.
## Scope
Stage 06e exit checklist.
## Out of Scope
Later stage implementation.
## Dependencies
TASK-06E-001, TASK-06E-002.
## Relevant Files
Stage 06e Tests/acceptance and Exit checklist; `docs/PROGRESS.md`.
## References
`docs/EXECUTION_STANDARD.md`, `docs/AGENTS.md`.
## Implementation Requirements
Run all required guards and record all findings.
## Acceptance Criteria
Every cross-company entry point tested for link and scope refusal.
## Tests Required
Full suite, migration Down, multi-company/trading-group guards, and seed.
## Edge Cases
Stale link cache and token mismatch.
## Security Considerations
No operation can bypass the choke point.
## Architectural Impact
Closes the R13 foundation.
## Definition of Done
Stage evidence is complete.
## Follow-up

# TASK-07C-001 — Implement group receipting and allocation

## Status
NOT_STARTED
## Stage
Stage 07c
## Objective
Capture one group receipt and allocate it across company ledgers through a saga.
## Why
One customer payment must settle multiple companies without a cross-database transaction.
## Scope
Receipt allocation, holds/confirmation integration, idempotency, APIs, and per-company postings.
## Out of Scope
General finance redesign.
## Dependencies
Stages 06c, 06d, 06e, 07.
## Relevant Files
`docs/stages/STAGE-07c-cross-company-money.md`, `docs/MULTI_COMPANY.md` §7.
## References
ADR-104, ADR-105, ADR-116.
## Implementation Requirements
Each leg posts in its own company database; failures compensate and never leave an untracked allocation.
## Acceptance Criteria
One receipt allocates correctly across companies; replay is idempotent; registry outage follows specified refusal/degradation.
## Tests Required
Saga, posting, concurrency, replay, and outage tests.
## Edge Cases
Partial allocation, currency, rounding remainder, and company failure.
## Security Considerations
Link/scope and financial authorization checks.
## Architectural Impact
Introduces group money saga.
## Definition of Done
Per-company books reconcile and group receipt is auditable.
## Follow-up

# TASK-07C-002 — Implement inter-company clearing and consolidated reporting

## Status
NOT_STARTED
## Stage
Stage 07c
## Objective
Post inter-company clearing and expose per-company statements plus labelled consolidated views.
## Why
Settlement needs balanced books and transparent aggregation.
## Scope
Clearing entries, reporting read models, `AsAt`, currency/tax rules, and APIs.
## Out of Scope
Vendor billing.
## Dependencies
TASK-07C-001.
## Relevant Files
Stage 07c Deliverables and `docs/MULTI_COMPANY.md` §§7–8.
## References
ADR-104–106, ADR-116.
## Implementation Requirements
No consolidated figure hides company identity or stale contributors; vendor books never enter tenant books.
## Acceptance Criteria
Company statements reconcile and consolidated output is labelled and reproducible.
## Tests Required
Money/tax, clearing, reporting, stale/read-model, and API tests.
## Edge Cases
Unequal currencies, rounding, compensation, and missing company.
## Security Considerations
Per-company report authorization.
## Architectural Impact
Extends Stage 07 accounting with saga clearing.
## Definition of Done
Clearing and reports are balanced and auditable.
## Follow-up

# TASK-07C-003 — Complete Stage 07c verification

## Status
NOT_STARTED
## Stage
Stage 07c
## Objective
Run money-and-tax, multi-company, migration, seed, and regression verification.
## Why
Financial cross-company work requires independent review.
## Scope
Stage 07c exit checklist.
## Out of Scope
Later modules.
## Dependencies
TASK-07C-001, TASK-07C-002.
## Relevant Files
Stage 07c Exit checklist; `docs/PROGRESS.md`, `docs/SYNC_AND_BACKUP.md`.
## References
`docs/AGENTS.md`, `docs/EXECUTION_STANDARD.md`.
## Implementation Requirements
Close all findings and prove migration reversibility and seed reconciliation.
## Acceptance Criteria
All checklist items have evidence.
## Tests Required
Full suite, migration Down, guards, and seed.
## Edge Cases
Saga partial failure and stale projection.
## Security Considerations
Financial and company isolation review.
## Architectural Impact
Closes Stage 07c.
## Definition of Done
Ready for 08c.
## Follow-up

# TASK-08B-001 — Build design tokens and theme foundation

## Status
NOT_STARTED
## Stage
Stage 08b
## Objective
Create the specified light/dark design tokens, typography, spacing, color, and accessibility foundation.
## Why
All future desktop UI must share one design system.
## Scope
`docs/DESIGN_SYSTEM.md` requirements and reusable theme assets.
## Out of Scope
Feature screens and WPF till behavior.
## Dependencies
Stage 08; Windows/WPF execution environment.
## Relevant Files
`docs/stages/STAGE-08b-design-system.md`, `docs/DESIGN_SYSTEM.md`, desktop project when created.
## References
Stage 08b full specification.
## Implementation Requirements
Implement only tokens and components explicitly specified there.
## Acceptance Criteria
Themes render consistently and meet documented contrast/touch/accessibility rules.
## Tests Required
Theme/component tests appropriate to the stage and Windows environment.
## Edge Cases
Dark/light switching, high DPI, touch sizing.
## Security Considerations
None beyond safe UI rendering.
## Architectural Impact
UI foundation used by POS and admin surfaces.
## Definition of Done
Stage specification and checklist are satisfied.
## Follow-up

# TASK-08B-002 — Complete design-system verification

## Status
NOT_STARTED
## Stage
Stage 08b
## Objective
Verify design-system assets and document the Windows environment result.
## Why
The roadmap marks this stage blocked on Windows/WPF.
## Scope
Stage 08b acceptance and exit evidence.
## Out of Scope
Feature implementation.
## Dependencies
TASK-08B-001.
## Relevant Files
Stage 08b document; `docs/DESIGN_SYSTEM.md`.
## References
Stage 08b Exit checklist.
## Implementation Requirements
Record any environment blocker without inventing UI requirements.
## Acceptance Criteria
All defined design-system checks are evidenced.
## Tests Required
Windows UI/component checks.
## Edge Cases
Unavailable WPF toolchain.
## Security Considerations
None.
## Architectural Impact
None beyond UI conventions.
## Definition of Done
Stage is verified or explicitly blocked by environment.
## Follow-up

# TASK-08C-001 — Implement availability and reservation ledger

## Status
NOT_STARTED
## Stage
Stage 08c
## Objective
Provide cross-company available-to-promise and reservation operations backed by the stock ledger.
## Why
Orders and field sales need commitments that never make stock negative.
## Scope
Availability, reservation ledger, expiry/release, company-link checks, and read APIs.
## Out of Scope
Order documents and mixed baskets.
## Dependencies
Stages 06c, 06d, 06e, 08.
## Relevant Files
`docs/stages/STAGE-08c-availability-sourcing.md`, `docs/MULTI_COMPANY.md` §§4–5.
## References
ADR-102, ADR-103, ADR-108, ADR-116, ADR-119.
## Implementation Requirements
Reservations are explicit, company-scoped, idempotent, and use live company data before commit.
## Acceptance Criteria
Availability never goes negative under competing demand and stale group views cannot commit.
## Tests Required
Concurrency, reservation lifecycle, stale read, outage, and stock-ledger integration tests.
## Edge Cases
Expiry, partial release, company outage, and competing reservations.
## Security Considerations
Active link and scope required for cross-company access.
## Architectural Impact
Becomes the reservation service consumed by Stage 14.
## Definition of Done
Reservation contract is proven under concurrency.
## Follow-up

# TASK-08C-002 — Implement sourcing and split fulfilment

## Status
NOT_STARTED
## Stage
Stage 08c
## Objective
Build sourcing plans across companies and split fulfilment into per-company operations/invoices.
## Why
One customer demand can be fulfilled without any company going negative.
## Scope
Sourcing algorithm, saga legs, company-link scope, reservation confirmation/release, and output contract.
## Out of Scope
Invoice implementation in 10c.
## Dependencies
TASK-08C-001, Stage 06d saga.
## Relevant Files
Stage 08c deliverables and `docs/MULTI_COMPANY.md` §5.
## References
ADR-102, ADR-103, ADR-108.
## Implementation Requirements
Plan is deterministic and each supplying company's stock movement stays in its own database.
## Acceptance Criteria
Demand splits correctly, no company is negative, and failed legs compensate.
## Tests Required
Multi-company sourcing, reserve/confirm, compensation, and partial-availability tests.
## Edge Cases
Equal candidates, insufficient total stock, and one company failure.
## Security Considerations
Do not source from an unlinked company.
## Architectural Impact
Shared service for orders and field sales.
## Definition of Done
Split fulfilment is independently callable and tested.
## Follow-up

# TASK-08C-003 — Complete Stage 08c verification

## Status
NOT_STARTED
## Stage
Stage 08c
## Objective
Run required availability/multi-company guards, migrations, seed, and full regression.
## Why
Reservations are a correctness-critical shared dependency.
## Scope
Stage 08c exit checklist.
## Out of Scope
Stage 14 changes.
## Dependencies
TASK-08C-001, TASK-08C-002.
## Relevant Files
Stage 08c Exit checklist; `docs/DATA_MODEL.md`, `docs/SYNC_AND_BACKUP.md`, `docs/PROGRESS.md`.
## References
`docs/AGENTS.md` and `docs/EXECUTION_STANDARD.md`.
## Implementation Requirements
Demonstrate per-company reservations, split fulfilment, and migration reversibility.
## Acceptance Criteria
All exit checks have evidence and no stale projection can authorize a write.
## Tests Required
Full suite, migration Down, guards, and seed.
## Edge Cases
Outage and compensation during seed scenario.
## Security Considerations
Company and tenant isolation review.
## Architectural Impact
Closes availability foundation.
## Definition of Done
Ready for 09b/14b.
## Follow-up

