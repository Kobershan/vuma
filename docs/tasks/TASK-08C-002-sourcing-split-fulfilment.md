# Task

## Status

COMPLETE

## Stage

Stage 08c — Cross-company availability, reservations & split fulfilment

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE, API, TESTING

## Objective

Build the cross-company half of Stage 08c on top of TASK-08C-001's ledger: plan a sourcing
allocation from the (stale-by-construction) group projection, commit it as a saga of one
serialisable reservation leg per company with compensation by release rows, re-source once on
a short leg then backorder, expire stale holds on a schedule — and turn a committed plan into
one document per supplying company sharing a `GroupDocumentRef`, reconciled line for line and
cent for cent.

## Why

TASK-08C-001 answers "how much can I sell" inside one company. This task answers "which
company's stock fills this line" across databases without ever letting a stale projection
cause an oversell (ADR-102's two-step: PLAN from the group view, COMMIT in the owning
company's database). It is also the seam Stage 14's rework, 14b approvals (ADR-108) and 09b
baskets will consume — so the strategy stays behind `ICompanySourcingStrategy` and the commit
stays a saga with idempotent legs, not a two-database transaction.

## Scope

- Domain (`src/VumaRetail.Domain/Inventory/`):
  - `SourcingPlan` / `SourcingAllocation` — per order line: demanded, per-(company, location)
    planned quantities, backordered remainder. `Backorder` is explicit data, never a null.
  - `SourcingDemandLine` — item/variant, quantity, unit price + currency snapshot (price is
    carried for the split documents' reconciliation; this stage never prices — Stage 10 owns
    pricing, ADR-075/138 snapshot discipline applies to what is carried).
- Application (`src/VumaRetail.Application/Inventory/`):
  - `ICompanySourcingStrategy` + default `AvailabilityThenProximity`: ordering company first,
    then other link-permitted companies by configured proximity rank, then most available.
    Pure function `(demands, groupView, orderingCompany, proximityRank) → SourcingPlan` —
    determinism is unit-testable with the stage's own numbers (12+8, 15+5-backorder). Never
    allocates against a company with none (business rule 3). Proximity input is an explicit
    ranked store list (13b geography does not exist yet); ties break by company code so output
    is total, not row-order-dependent.
  - `PlanSourcingQuery` (dry run any caller can request before committing — no side effects,
    `[CommandSideEffect]`-free, never writes).
  - `CommitSourcingPlanCommand` → `SourcingCommitService` (an application *service*, NOT a
    command handler — a handler may resolve at most one company context, MultiCompanyGuardTests):
    1. `RequireLink(orderingCompany, eachSupplier, SharedSourcing)` at the point of use
       (ADR-122) + register the row in `TradingGroupGuardTests` EntryPoints.
    2. Write immutable `SagaIntent` (type `availability.sourcing-commit`) + one `SagaLeg` per
       supplying company in the registry — the intent records the full plan before anything
       happens (ADR-116).
    3. Execute each leg through `ICompanyReservationGateway.ReserveLegAsync` — one serialisable
       transaction in that company's own database re-reading real availability and holding
       `min(available, planned)`; legs idempotent on `(intent_id, leg_id)` (the partial unique
       index from TASK-08C-001 makes retry safe).
    4. Short leg → re-source the shortfall once against remaining linked companies → still
       short → backorder line. Never a negative, never an oversell from stale data.
    5. Unrecoverable leg failure → compensate acknowledged legs with Release rows (new
       entries, never deletes), mark intent Compensated, surface the refusal; the order is not
       created.
    6. All legs acknowledged → mark intent Completed; return committed plan + reservation ids.
  - `ISplitDocumentBuilder`: committed plan + source order lines → one `SalesOrder` per
    supplying company (built through `SalesOrder`'s existing factories, each carrying
    `GroupDocumentRef` = source order number), each written entirely inside its own company
    database. Asserts: every source line lands on exactly one document, per-document sums
    equal source sums, no rounding drift (sums compared in minor units… carried at
    `decimal(18,4)` with currency — the assert compares exact decimals, not rounded display).
  - Reservation expiry: `IReservationExpiryPolicy` (per-tenant override table
    `inventory.reservation_expiry_policies`: source-document type → hours, NULL = never;
    defaults: Order 72h, ProFormaApproval 72h, Transfer none, Shipment none) +
    `ReservationExpiryService.ExpireDueAsync` (one company at a time; expiry writes ledger
    rows so history explains itself — business rule 5) + thin `ReservationExpiryHostedService`
    iterating Active companies each in its own scope (own transaction per company — a failing
    company never blocks the others; failures are per-company results, fan-out style).
  - `SalesOrder` additive change only: nullable `GroupDocumentRef` (+ config + migration in
    the orders mapping). No behaviour change to Stage 14; its rework onto this ledger is
    recorded future work, not this task.
- Infrastructure: strategy, commit service, gateway production implementation (scope-per-leg
  via `IServiceScopeFactory`: set company, create context, run, dispose — each leg its own
  operation, honouring `CompanyDbContextFactory`'s one-context rule), split builder, expiry
  policy/service/hosted service, DI wiring, migration for `GroupDocumentRef` +
  `reservation_expiry_policies` (reversible).
- API: `POST /api/v1/sourcing/plan` (dry run), `POST /api/v1/sourcing/commit` (saga-backed),
  `GET /api/v1/sourcing/intents/{id}` (intent + leg states for the in-flight report).
  OpenAPI examples + errors; `AsAt` on every figure.
- Seed: extend the TASK-08C-001 seed to a full two-company sourced order — reserved in both
  companies, visible in group availability (`DemoSeed`).

## Out of Scope

- Changing `SagaCoordinator.DispatchLegAsync` or any 06d/06e/07c saga behaviour. The commit
  drives the 06d saga *records* (`SagaIntent`/`SagaLeg`) directly because dispatch is a
  documented no-op pending 07C-004, whose plan keeps its dispatch table in 07c. When shared
  dispatch lands, `ICompanyReservationGateway` is written to plug into it (one intent type,
  one handler); the convergence is recorded as a follow-up, not done here.
- Credit holds (ADR-101/108 sequencing belongs to 14b approval, which will call this task's
  commit after taking its hold).
- Pricing, tax, promotions inside segments (10/10c/09b territory). Money carried is snapshot
  only.
- Stage 14 rework, 09b, 13b, 14b.

## Architecture

- ADR-102 (plan-stale/commit-local) is the whole design; ADR-103 (local reservations);
  ADR-108 (approval-shaped saga: reserve per company, compensate in reverse — reused here
  without the credit-hold step, which is 14b's); ADR-116 (immutable intent, idempotent legs,
  compensation by new document, alarm on unacknowledged legs via `IAlarmService`);
  ADR-119 (group reads plan only).
- ADR-122: `RequireLink(SharedSourcing)` at commit time + guard-test row. Planning (dry run)
  does NOT require the link — a planner showing "sister company has 30" is a read, and reads
  degrade to own-company when unlinked (MULTI_COMPANY.md §3 precedent: registry down → keep
  trading locally). Commit requires it. The asymmetry is deliberate and recorded here.
- §7 rule 20: one company, one database, one transaction — enforced by construction (gateway
  opens exactly one company context per leg; the static `.CreateAsync(` scan stays at one call
  site per file).
- §7 rule 12: the split builder names no GL account (orders post no journals here).

## Architectural Boundaries

- Same as TASK-08C-001, plus: new cross-company entry point registered in
  `TradingGroupGuardTests`; no new project references (sourcing lives in Inventory
  Application/Infrastructure, consuming Orders *domain* types + published ports only —
  LayeringTests + module-boundary tests stay green).

## Dependencies

TASK-08C-001 (ledger, projection, gateway seams). Stages 06c/06d/06e/08 on `main`.

## Relevant Files

- TASK-08C-001's files, plus:
- `src/VumaRetail.Domain/Orders/SalesOrder.cs`, `SalesOrderLine.cs` (factories the split
  builder uses), `src/VumaRetail.Application/Orders/OrderAllocation.cs` (read-only reference
  for line semantics — do not modify Stage 14 behaviour)
- `src/VumaRetail.Domain/Registry/RegistryRecords.cs` (`SagaIntent`/`SagaLeg`),
  `src/VumaRetail.Infrastructure/Registry/RegistryServices.cs` (factory whose one-context
  rule the gateway honours), `CompanyLinkService.cs` (`RequireLink`)
- `src/VumaRetail.Application/Abstractions/Registry/CompanyLinkService.cs`
- `tests/VumaRetail.ArchitectureTests/TradingGroupGuardTests.cs` (add row)

## Relevant Documentation

Stage doc, `docs/MULTI_COMPANY.md` §4–§5, ADR-102/103/108/116/119/122, `docs/TRADING_GROUP.md`
§2 (entry-point checklist), `CLAUDE.md` §7 rules 7/12/20/21.

## Implementation Requirements

- Strategy purity: `AvailabilityThenProximity.Plan(...)` takes only data (demands, group view
  rows with per-company available, ordering company, proximity rank) — no DB, no clock. All
  four acceptance numbers are unit tests.
- Commit service testability: `ICompanyReservationGateway` has exactly two implementations —
  production (scope-per-leg) and test (named test databases). No conditional test hooks in
  production code.
- The re-source-once path reuses the same leg machinery with a new `leg_id` (retries of the
  SAME leg reuse its id; a re-sourced remainder is a NEW leg — idempotency keys must not
  conflate the two).
- Split reconciliation assert runs inside the builder and throws
  `SplitReconciliationException` (lists the first mismatching line) rather than returning a
  boolean nobody checks.
- Expiry hosted service: interval + batch size from `ReservationExpiryOptions`; per-company
  errors captured, job continues, alarm on repeated failure (same `IAlarmService`).

## Data/Database Impact

- Migration: `orders.sales_orders.group_document_ref` (nullable text + index),
  `inventory.reservation_expiry_policies`. Reversible. No other table changes.

## API Impact

- `POST /api/v1/sourcing/plan`, `POST /api/v1/sourcing/commit`,
  `GET /api/v1/sourcing/intents/{intentId}` in OpenAPI with examples + errors.

## Security

- `inventory.reservation.manage` on commit; planning needs `inventory.availability.view` (+
  `registry.availability.view` when the request spans companies). Intent query discloses only
  the tenant's own intent (tenant predicate, not just id lookup).

## Multi-Company/Tenant Impact

- This task IS the cross-company path: link check, per-company legs, group ref, AsAt
  everywhere. Same-Operator-ID invariant is enforced by the link itself (ADR-121/122), not
  re-checked here.

## Sync/Offline Impact

- Reservation rows from commit legs replicate like any reservation (TASK-08C-001). Saga
  intents/legs live in the registry and follow the registry's own replication. Offline terminals
  are out of scope (ordering happens online against the store server; R1's offline sale path is
  untouched).

## Acceptance Criteria

1. 20 demanded, A=12 + B=30 → plan 12+8, commit holds 12+8, no backorder, B never negative.
2. 20 demanded, A=12 + B=3 → 15 held, 5 backordered, nothing negative anywhere.
3. Stale projection (planner told B=30, B really 3) → 3 held in B, remainder re-sourced then
   backordered; available never negative, never an oversell.
4. Two-company commit, second leg fails → first leg compensated by a Release row, intent
   Compensated, no order created, availability in both companies exactly where it started.
5. Committed two-company plan → two `SalesOrder`s sharing one `GroupDocumentRef` whose lines
   sum exactly to the source (line coverage + money sums).
6. Expiry: a hold past its 72h policy expires into an Expired row and frees available; a
   never-policy hold never expires.
7. Coverage ≥ 80% on this task's Domain + Application (measured in TASK-08C-003).

## Tests Required

- Unit: strategy (criteria 1–2 as pure plans; tie-break determinism; never-allocate-against-none);
  split reconciliation (exact sums, cent-exact multi-line, mismatch throws with the line named).
- Integration (real PG, two databases for company A/B + registry reads): criteria 3–5
  (criterion 4 injects the failure by revoking available in B between plan and commit — a
  genuinely stale projection, not a mock flag); expiry policy test with a controllable clock.
- Architecture: `TradingGroupGuardTests` extended row green; full suite green.

## Edge Cases

- Ordering company has zero of the SKU: plan skips it openly (allocation rows only where
  planned > 0), commit legs only for suppliers.
- All companies short: whole line backordered, intent still Completed (a backorder is a
  successful answer, not a failure) with zero legs or legs holding partials — decide in
  implementation, record here: legs only for held quantities; a fully uncovered line creates
  no leg.
- Commit requested twice with the same idempotency key → same intent returned, no second set
  of holds (intent `IdempotencyKey` unique per tenant — check registry constraint exists, add
  if missing… verify during implementation).
- Company deactivated mid-saga → leg fails → compensate + backorder (serving guard refusal is
  a normal leg failure, not a crash).

## Definition of Done

- [ ] Build green (Release, zero warnings Domain/Application), tests green, coverage ≥ 80%
- [ ] Migration reversible, `Down` tested
- [ ] Endpoints in OpenAPI with examples + errors; intent states visible for ops
- [ ] `TradingGroupGuardTests` row added and green; no other architecture violations
- [ ] Seed: two-company sourced order reserved + visible in availability
- [ ] Work Log complete; PROGRESS/CURRENT updated; committed

## Follow-up Findings

- Converge `ICompanyReservationGateway` onto the shared saga dispatch table when 07C-004 lands
  it (this task's gateway is written as one intent type + one handler for exactly that).
- Stage 14 rework onto this ledger (COD ADR-111, geography ADR-113, reservations ADR-103) —
  Stage 14's problem, consuming this task's ports.

## Work Log

2026-09-07: Implementation complete. Domain (`SourcingPlan`, `SourcingAllocation`, `SourcingDemandLine`), Application (`AvailabilityThenProximity` strategy, `PlanSourcingQuery`, `CommitSourcingPlanCommand` → `SourcingCommitService`, `ISplitDocumentBuilder`, `IReservationExpiryPolicy`, `ReservationExpiryService`, `ReservationExpiryHostedService`, `SourcingPlannerService`), Infrastructure (all production implementations, DI wiring, `GroupDocumentRef` + `reservation_expiry_policies` migrations — both reversible), API (`POST /api/v1/sourcing/plan`, `POST /api/v1/sourcing/commit`, `GET /api/v1/sourcing/intents/{id}`), seed (two-company sourced order). Unit tests (strategy, split reconciliation, commit service, expiry), integration tests (commit flow, stale projection, two-company commit failure, expiry), architecture (`TradingGroupGuardTests` row added). Build green, 1010 unit + 54 architecture all green, 23 inventory integration tests green on real PostgreSQL. `SalesOrder.GroupDocumentRef` nullable + index migration applied. Commit pushed to main as `d88f112`.
