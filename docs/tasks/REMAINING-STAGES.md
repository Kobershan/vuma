# TASK-09-001 — Verify reopened POS and hardware stage

## Status
NEEDS_VERIFICATION
## Stage
Stage 09
## Objective
Independently verify the reopened POS defects, entitlement gate, hardware/API behavior, and deferred WPF boundary.
## Why
Progress says fixes are present but the required agent panel has not rerun.
## Scope
Stage 09 acceptance and outstanding review/coverage/OpenAPI items.
## Out of Scope
WPF till implementation and unrelated later stages.
## Dependencies
Stages 06–08; Stage 08b for the deferred shell.
## Relevant Files
`docs/stages/STAGE-09-pos-terminal.md`, `docs/PROGRESS.md` §§4.10–4.16, POS source/tests.
## References
Stage 09 exit checklist; `docs/AGENTS.md`; `docs/LICENSING.md`; ADR-135–138.
## Implementation Requirements
Verify offline idempotency, read-only carve-outs, module gate, currency, rounding, permission, OpenAPI, replication, and coverage claims without reimplementing completed fixes.
## Acceptance Criteria
Required panel passes or each failure is recorded; stage status can then be resolved.
## Tests Required
Full suite plus stage-verifier, sync-and-offline, licence-safety, money-and-tax, and OpenAPI/replication coverage checks.
## Edge Cases
Offline replay, weighted rounding boundary, foreign currency, and no network.
## Security Considerations
Terminal/device auth and licence enforcement.
## Architectural Impact
Verification only; no redesign.
## Definition of Done
All open evidence gaps are resolved or explicitly tracked.
## Follow-up

# TASK-09B-001 — Implement mixed-basket transaction and per-company invoices

## Status
BLOCKED
## Stage
Stage 09b
## Objective
Process one till/customer basket across linked companies with one invoice per company and one payment allocation.
## Why
R13 requires separate company books with one non-fiscal basket summary.
## Scope
Basket, SharedTill/link checks, per-company tax/rounding, payment allocation, summary, and APIs.
## Out of Scope
New reservation, company-link, or invoice engines.
## Dependencies
Stage 09 verification; Stages 06e, 07c, 08c, and 10c.
## Relevant Files
`docs/stages/STAGE-09b-mixed-basket.md`, `docs/TRADING_GROUP.md` §4.
## References
ADR-125, ADR-126, ADR-128; Stage 09b business rules.
## Implementation Requirements
Every company is checked at point of use; tax and rounding are per company; payment is allocated through group money; no cross-database transaction.
## Acceptance Criteria
Mixed basket produces separate balanced invoices and a linked non-fiscal summary; retry creates no duplicate.
## Tests Required
Tax/rounding, link scope, payment allocation, retry, outage, and receipt tests.
## Edge Cases
One company unavailable, refund, rounding remainder, and shared till scope removed mid-flow.
## Security Considerations
Terminal/company authorization and fiscal document separation.
## Architectural Impact
Composes 06e/07c/08c/10c; creates no parallel engines.
## Definition of Done
Unblocked only after dependencies are verified and all acceptance tests pass.
## Follow-up

# TASK-10-001 — Verify reopened sales and promotions stage

## Status
NEEDS_VERIFICATION
## Stage
Stage 10
## Objective
Verify returns concurrency/idempotency, entitlement gating, OpenAPI examples, replication coverage, and agent review findings.
## Why
Progress records fixes but leaves review/coverage gaps.
## Scope
Stage 10 outstanding evidence only.
## Out of Scope
Rebuilding completed pricing/promotion behavior.
## Dependencies
Stage 09 verification.
## Relevant Files
`docs/stages/STAGE-10-sales-promotions.md`, `docs/PROGRESS.md` §§4.21–4.24, sales source/tests.
## References
Stage 10 Exit checklist; ADR-074 and relevant review entries.
## Implementation Requirements
Confirm return row locking and client-id retry behavior, `RequireModule("sales")`, OpenAPI examples, and replication round trips.
## Acceptance Criteria
The documented review and coverage gaps are independently resolved or precisely recorded.
## Tests Required
Money-and-tax review, full suite, concurrency, OpenAPI, and replication tests.
## Edge Cases
Concurrent duplicate returns and replayed webhooks/commands.
## Security Considerations
Sales entitlement and tenant isolation.
## Architectural Impact
Verification only.
## Definition of Done
Stage status is supported by current evidence.
## Follow-up

# TASK-10B-001 — Implement customer credit accounts and lay-by

## Status
NOT_STARTED
## Stage
Stage 10b
## Objective
Implement customer credit accounts and lay-by lifecycle with correct liability and stock treatment.
## Why
Customers can pay over time while accounting and available stock remain correct.
## Scope
Domain, persistence, commands, statements, postings, reservations, API, and tests for accounts/lay-by.
## Out of Scope
Stokvel group savings.
## Dependencies
Stages 07, 09, 10.
## Relevant Files
`docs/stages/STAGE-10b-accounts-layby-stokvel.md`, `docs/DATA_MODEL.md` savings section.
## References
Stage 10b Deliverables/business rules/tests; ADR-055.
## Implementation Requirements
Use existing posting/reservation services; reserved stock is excluded from available stock; balances reconcile to the cent.
## Acceptance Criteria
End-to-end account and lay-by flows produce correct postings, statements, reservations, and replay behavior.
## Tests Required
Ledger, liability, stock availability, offline capture, statement, and API tests.
## Edge Cases
Cancellation, overdue installment, partial payment, and refund.
## Security Considerations
Customer financial data and permissioned statements.
## Architectural Impact
Adds customer-money module without duplicating finance.
## Definition of Done
Account and lay-by acceptance scenarios pass.
## Follow-up

# TASK-10B-002 — Implement stokvels and complete Stage 10b verification

## Status
NOT_STARTED
## Stage
Stage 10b
## Objective
Implement group savings/buying, contributions/reconciliation, statements, and the complete exit evidence.
## Why
Stokvel liabilities and reserved stock require the same cent-accurate controls.
## Scope
Stokvel domain/application/API, offline reconciliation, seed, migration and stage checks.
## Out of Scope
General customer credit redesign.
## Dependencies
TASK-10B-001.
## Relevant Files
Stage 10b Stokvel deliverable, Tests / acceptance, and Exit checklist.
## References
ADR-055; `docs/EXECUTION_STANDARD.md`.
## Implementation Requirements
Member/group statements print/email/display; contributions reconcile; reserved stock remains unavailable.
## Acceptance Criteria
All three schemes are demonstrable with correct postings and every exit box is evidenced.
## Tests Required
Stokvel lifecycle, offline reconciliation, statements, migration Down, full suite, and guards.
## Edge Cases
Late contribution, member removal, and partial fulfilment.
## Security Considerations
Member privacy and financial authorization.
## Architectural Impact
Extends customer-money behavior.
## Definition of Done
Stage 10b is complete and verified.
## Follow-up

# TASK-11-001 — Verify data-import stage gaps

## Status
NEEDS_VERIFICATION
## Stage
Stage 11
## Objective
Resolve the outstanding agent-review and Domain/Application coverage evidence for the existing import implementation.
## Why
The implementation is present, but Progress records 76.80% coverage and missing reviews.
## Scope
Review findings, coverage floor, and any required documentation evidence.
## Out of Scope
Rebuilding import behavior already marked complete.
## Dependencies
Stage 06; Stage 08/10 targets as documented.
## Relevant Files
`docs/stages/STAGE-11-data-import.md`, `docs/PROGRESS.md` §4.17/§4.24, import tests.
## References
Stage 11 Tests/acceptance and Exit checklist; `docs/IMPORT_PIPELINE.md`; ADR-076–080.
## Implementation Requirements
Do not claim complete below the 80% floor; run the specified six-agent review and address or record findings.
## Acceptance Criteria
Coverage reaches the stated floor and all outstanding review items have evidence.
## Tests Required
Full suite, coverage, import commit/rollback, migration, OpenAPI, and agent panel.
## Edge Cases
Rollback retry and OCR stub behavior.
## Security Considerations
Tenant file isolation and secret-safe import logs.
## Architectural Impact
Verification only.
## Definition of Done
Status is confidently resolved.
## Follow-up

# TASK-12-001 — Verify reopened procurement stage

## Status
NEEDS_VERIFICATION
## Stage
Stage 12
## Objective
Verify permission-enforcement coverage and all previously fixed idempotency, posting, and matching behavior.
## Why
Progress leaves the promised permission tests open.
## Scope
Outstanding review/checklist evidence only.
## Out of Scope
Rebuilding procurement.
## Dependencies
Stages 08 and 11.
## Relevant Files
`docs/stages/STAGE-12-procurement.md`, `docs/PROGRESS.md` §4.20, procurement tests.
## References
Stage 12 Exit checklist; ADR-081–086, ADR-140.
## Implementation Requirements
Confirm every high-risk permission denial, idempotency, match net-value labeling, and receipt contract.
## Acceptance Criteria
Open checklist item is evidenced and no regression is found.
## Tests Required
Full suite, permission, money-and-tax, migration, OpenAPI, replication, and seed tests.
## Edge Cases
Duplicate GRN/invoice payment and tolerance boundaries.
## Security Considerations
RBAC and financial audit.
## Architectural Impact
Verification only.
## Definition of Done
Stage can leave REOPENED state.
## Follow-up

# TASK-13-001 — Verify reopened warehouse stage

## Status
NEEDS_VERIFICATION
## Stage
Stage 13
## Objective
Confirm the implemented reservation, idempotency, cycle-count, entitlement, permission, and replication behavior.
## Why
Progress records fixes but stage remains reopened pending evidence.
## Scope
Outstanding review/coverage/OpenAPI/replication evidence.
## Out of Scope
Consolidated picking in Stage 13b.
## Dependencies
Stage 08.
## Relevant Files
`docs/stages/STAGE-13-warehouse-management.md`, `docs/PROGRESS.md` §4.19/§4.23, warehouse tests.
## References
Stage 13 Exit checklist; ADR-087–091, ADR-134.
## Implementation Requirements
Verify available-vs-reserved stock, command retry IDs, intervening pick/count behavior, and all high-risk permissions.
## Acceptance Criteria
All open stage evidence is independently green or recorded.
## Tests Required
Full suite, stock-availability-guard, migration, OpenAPI, replication, and seed.
## Edge Cases
Pick allocation during count and duplicate movement commands.
## Security Considerations
Warehouse permission and stock isolation.
## Architectural Impact
Verification only.
## Definition of Done
Stage status reflects verified evidence.
## Follow-up

# TASK-13B-001 — Implement consolidated waves and staging

## Status
NOT_STARTED
## Stage
Stage 13b
## Objective
Build geographically and temporally filtered consolidated pick waves grouped by SKU with staging states.
## Why
Pickers need one walk for a town while each company's stock remains separate.
## Scope
Wave build/preview/release, geography snapshot, consolidation/staging/packing/dispatch, and APIs.
## Out of Scope
New stock ledger or order allocation engines.
## Dependencies
Stages 13, 14, 06e, 08c.
## Relevant Files
`docs/stages/STAGE-13b-picking-waves-staging.md`, Stage 13/14 docs, `docs/TRADING_GROUP.md`.
## References
ADR-113–115, ADR-087–091.
## Implementation Requirements
Record wave geography/period; route each movement to its own company database; apply active picking scope.
## Acceptance Criteria
Preview/release is deterministic and consolidated staging never merges company books.
## Tests Required
Wave filtering, grouping, staging movement, retry, company scope, and API tests.
## Edge Cases
Order changed after snapshot, partial pick, and company outage.
## Security Considerations
Warehouse scope and audit.
## Architectural Impact
Extends warehouse fulfillment with cross-company orchestration.
## Definition of Done
Wave lifecycle is independently executable.
## Follow-up

# TASK-13B-002 — Implement interval counts and complete Stage 13b

## Status
NOT_STARTED
## Stage
Stage 13b
## Objective
Schedule slow-mover counts, warn about in-flight stock, and complete stage verification/seed.
## Why
Counts must not misstate stock during picking.
## Scope
Scheduled count, warning, finalization, migration/docs, seed, and exit checks.
## Out of Scope
General stocktake redesign.
## Dependencies
TASK-13B-001.
## Relevant Files
Stage 13b interval-count and Exit checklist sections; `docs/DATA_MODEL.md` §4k.
## References
ADR-114–115; `docs/AGENTS.md`.
## Implementation Requirements
Warn when stock is in flight and preserve per-company movement/audit rules.
## Acceptance Criteria
Durban wave, consolidation, and in-flight interval count seed passes; all exit items evidenced.
## Tests Required
Count scheduling/finalization, warning, migration Down, full suite, guards, and seed.
## Edge Cases
Intervening pick, cancellation, and stale wave.
## Security Considerations
Warehouse and company isolation.
## Architectural Impact
Closes consolidated picking stage.
## Definition of Done
Stage ready for downstream use.
## Follow-up

# TASK-14-001 — Verify reopened order-management stage

## Status
NEEDS_VERIFICATION
## Stage
Stage 14
## Objective
Verify the implemented idempotency, reservation integration, entitlement, return concurrency, and permission coverage gaps.
## Why
Progress records unresolved return race and missing permission-denial tests.
## Scope
Outstanding evidence and review only.
## Out of Scope
Rebuilding orders or Stage 13b.
## Dependencies
Stages 10, 13, and 08c.
## Relevant Files
`docs/stages/STAGE-14-order-management.md`, `docs/PROGRESS.md` §4.21 and Stage 14 row.
## References
Stage 14 Exit checklist; ADR-092–096, ADR-139–141.
## Implementation Requirements
Confirm client-id retries, no-op replay, reservation path, return locking, entitlement, and per-operation permission tests.
## Acceptance Criteria
Known open gaps are closed or documented with an owner and dependency; status is evidence-based.
## Tests Required
Full suite, agent panel, concurrency, permission, OpenAPI, and seed tests.
## Edge Cases
Backorder reattempt and duplicate return-line addition.
## Security Considerations
Order/customer isolation and entitlement.
## Architectural Impact
Verification only.
## Definition of Done
Stage no longer relies on unverified claims.
## Follow-up

# TASK-14B-001 — Implement field-sales proposals and approval

## Status
NOT_STARTED
## Stage
Stage 14b
## Objective
Implement reps, territories, pro forma orders/credit notes, and management approval before posting or reserving.
## Why
Field sales is a proposal, never an implicit commitment.
## Scope
Domain, workflows through Stage 05 approval engine, APIs, audit, and idempotency.
## Out of Scope
New order, sourcing, or approval engines.
## Dependencies
Stages 14, 10c, 08c, 05.
## Relevant Files
`docs/stages/STAGE-14b-field-sales.md`, `docs/FIELD_SALES.md`, `docs/MULTI_COMPANY.md` §§4,6.
## References
ADR-103, 107–110, 112; CLAUDE §7 rules 12–13.
## Implementation Requirements
Capture availability but reserve/post only on approval; approval creates documents and commits stock through existing services.
## Acceptance Criteria
Unapproved proposals have no posting/reservation; approved split order creates correct per-company outcome.
## Tests Required
Proposal, approval, rejection, sourcing, idempotency, and performance tests.
## Edge Cases
Availability changes before approval, offline rep, and rejection after expiry.
## Security Considerations
Rep scope, customer consent, and approval authorization.
## Architectural Impact
Composes workflow, order, and sourcing services.
## Definition of Done
Rep flow is end-to-end without bypassing approval.
## Follow-up

# TASK-14B-002 — Complete field-sales verification and performance

## Status
NOT_STARTED
## Stage
Stage 14b
## Objective
Add rep performance/month comparisons and complete the stage seed, guards, migration, and documentation.
## Why
The stage requires operational visibility without changing proposal semantics.
## Scope
Performance queries, seed, exit checklist, replication/docs.
## Out of Scope
New analytics platform.
## Dependencies
TASK-14B-001.
## Relevant Files
Stage 14b Tests/acceptance and Exit checklist; `docs/DATA_MODEL.md` §4o.
## References
`docs/FIELD_SALES.md`; `docs/AGENTS.md`.
## Implementation Requirements
Seed two reps/territories and three pro formas including approved split, rejected, and pending cases.
## Acceptance Criteria
Required guards, migration Down, seed, and performance comparisons pass.
## Tests Required
Performance, full suite, guards, migration, API, and seed.
## Edge Cases
Month boundary and no activity.
## Security Considerations
Rep data scoped to permitted companies/territories.
## Architectural Impact
Completes field-sales module.
## Definition of Done
Stage 14b exit checklist is fully evidenced.
## Follow-up

