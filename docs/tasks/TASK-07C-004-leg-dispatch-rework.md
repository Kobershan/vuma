# Task

## Status

NOT_STARTED

## Stage

Stage 07c — Cross-company money: group receipting, allocation, inter-company clearing, consolidated reporting

## Type

APPLICATION, INFRASTRUCTURE, DATABASE, TESTING, VERIFICATION

## Objective

Close the five structural gaps TASK-07C-003 proved: make saga legs actually execute in the
target company's database, create clearing intents on the allocate path, dispatch reversing legs,
guard period close over outstanding intents, and prove it all against real databases.

## Why

Verification (TASK-07C-003, 2026-09-06) showed the stage records intents but moves no money:
`SagaCoordinator.DispatchLegAsync` is a no-op, both leg handlers are dead code, `ReverseAsync`
dispatches nothing, nothing creates clearing intents, and no period-close guard exists. Every
acceptance criterion that touches a company database fails structurally, not environmentally.

## Scope

- Route saga legs by intent type: `group-receipt-allocation` legs execute `GroupReceiptLegHandler`
  inside the target company DB (idempotent on `(group_receipt_id, allocation_id)`); keep the
  dispatch table in 07c, not in 06d's shared coordinator — 06d owns the mechanism, 07c owns its
  leg types. Do not break 06d/06e intent types.
- `GroupReceiptLegHandler.ApplyAllocationLegAsync`: persist the `ArReceipt` (save on the company
  context it creates, or take a unit of work — pick one, no silent loss), pass the real clearing
  `intentId` to `RecordFromGroup` instead of null.
- Allocate path: after legs acknowledge, create the paired `InterCompanyClearingIntent`(s) for any
  allocation outside the bank-owning company (business rule 3); bank side posts in the owning company.
- `ReverseAsync`: dispatch reversing legs to every involved company (business rule 6); never edit journals.
- `IPeriodCloseGuard` (or equivalent): period close refuses with outstanding intents and names them.
- `IGroupPaymentService` implementation + `/api/v1/group-payments` endpoints, or a written ADR
  deferring outbound to a later stage with the stage doc amended.
- `DemoSeed`: 3-company group, one shared customer, one group receipt allocated across all three;
  posting rules for `group.receipt.allocated`/`group.receipt.reversed` (and the pre-existing gap:
  `ar.receipt.posted`/`ap.payment.posted`).
- `tests/VumaRetail.IntegrationTests/Registry/GroupReceipt*`: the operator's R9 000 example across
  3 real databases, mid-allocation outage + retry-once, partial allocation, full reversal,
  idempotent retry, randomised 200-op net-zero run.
- Re-run `money-and-tax` + `multi-company-guard` reviews and the full exit checklist; measure
  per-stage coverage ≥80% on Domain + Application.

## Out of Scope

- Changing 06d's saga mechanism (states, timeouts, alarms) — consume it, don't redesign it.
- Group VAT aggregation (explicitly out per MULTI_COMPANY.md §8).
- Anything outside Stage 07c.

## Architecture

Leg dispatch must preserve ADR-116 (one company, one database, one transaction; idempotent legs;
compensation by new document) and ADR-122 (link check at point of use — already present in
`AllocateAsync`, keep it; add the same check to the payment path).

## Architectural Boundaries

- No handler opens transactions against two databases (architecture test enforces).
- No module names a GL account (§7 rule 12) — legs raise events, rules decide accounts.
- Clearing nets to zero at every instant; reconciliation job + alarm stay as the cross-DB assertion.

## Dependencies

TASK-07C-003 (verdict + evidence). Requires a machine with Docker/PostgreSQL for the DB-backed
acceptance criteria — record UNVERIFIED, not PASS, for anything not executed.

## Relevant Files

- `src/VumaRetail.Infrastructure/Registry/SagaCoordinator.cs` (`DispatchLegAsync`, line 73)
- `src/VumaRetail.Infrastructure/Registry/GroupReceiptLegHandler.cs` (no callers, no save)
- `src/VumaRetail.Infrastructure/Registry/GroupReceiptService.cs` (`ReverseAsync`, line 84)
- `src/VumaRetail.Infrastructure/Registry/NetZeroReconciliationJob.cs`
- `src/VumaRetail.Infrastructure/Registry/ConsolidationService.cs`
- `src/VumaRetail.StoreServer/DemoSeed.cs` (posting rules ~line 1429, finance seed)
- `src/VumaRetail.Domain/Registry/GroupReceiptEntities.cs`
- `src/VumaRetail.Domain/Finance/ArReceipt.cs` (`RecordFromGroup`), `ApPayment.cs`

## Relevant Documentation

`docs/stages/STAGE-07c-cross-company-money.md`, `docs/MULTI_COMPANY.md` §2/§7/§8, ADRs
099/104/105/106/116/122, `CLAUDE.md` §7 rules 6/7/12/20, `docs/TESTING.md`.

## Implementation Requirements

None beyond Scope — but every Scope bullet needs a test from the stage's acceptance list, and the
DB-backed ones must run against real PostgreSQL, not fakes.

## Data/Database Impact

- New migration only if the design needs new columns; prefer using the existing
  `group_document_id`/`intent_id` and saga tables.
- Migration must be reversible (`Down` executed, not just written).

## API Impact

- New `POST /api/v1/group-payments` (+ allocations) endpoints appear in OpenAPI with permission
  annotations and error responses, or the deferral ADR + amended stage doc.

## Security

- `registry.payment.*` permissions already registered — wire endpoints to the constants, as
  `GroupReceiptEndpoints`/`ConsolidationEndpoints` now do (no hardcoded strings).

## Multi-Company/Tenant Impact

This task *is* the multi-company money path: 3-database proof, clearing pairs, AsAt + stale
contributors on every consolidated figure (already implemented on the read side — keep it).

## Sync/Offline Impact

Company-leg receipts replicate normally from their company DBs (already attributed
`StoreToCloud/AppendOnly` on `ArReceipt`/`ApPayment`). Resolve the open question: group receipt
entities carry no `[Replicated]` while `DATA_MODEL.md` classifies them StoreToCloud — either
attribute them or correct the doc row with a reason.

## Acceptance Criteria

TASK-07C-003's criteria #1–#8, proven against real databases, plus #9 (measured coverage), #10
(`Down` executed), #11 (seed present), #12 (reviews closed).

## Tests Required

All scenarios under Scope, per `docs/TESTING.md` ratio (feature work ships with tests).

## Edge Cases

- Company DB down mid-allocation → leg Pending, visible with age, retry applies exactly once.
- Allocate-then-reverse-before-ack; reverse of partial; over-allocation across companies.
- Period close attempted with outstanding intent → refused, intent named.

## Definition of Done

- [ ] All TASK-07C-003 acceptance criteria pass against real PostgreSQL (or recorded UNVERIFIED
      with a reason that is environmental, not structural)
- [ ] `money-and-tax` + `multi-company-guard` reviews closed
- [ ] Migration reversible, `Down` executed
- [ ] Seed data present and exercised by at least one integration test
- [ ] Per-stage coverage ≥80% measured on Domain + Application
- [ ] `docs/PROGRESS.md` updated, ADRs appended if any, committed at green checkpoint

## Follow-up Findings

None yet.

## Work Log

Not started.
