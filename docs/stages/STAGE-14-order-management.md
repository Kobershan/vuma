# STAGE 14 — Order Management: omnichannel orders, allocation, backorders, click & collect, returns

**Status:** DONE — merged to `main` at `d35b5d8` (2026-08-21) · **Depends on:** 10 (price resolution —
every order line is priced through `IPriceResolver`, never a second pricing path) and 13 (warehouse —
`PickWave`/`PickTask` is this stage's fulfilment engine, not something it forks) · **Reference reading:**
`docs/DATA_MODEL.md` §1–§3, `docs/CONVENTIONS.md` §1–§6, `docs/API_STANDARDS.md` §3–§5 and §8–§9,
`docs/TESTING.md` §2–§3, `CLAUDE.md` §7 rules 1, 2, 3, 7, 8, 9, 12, 13, 19, `docs/stages/STAGE-10-sales-promotions.md`
(price resolution and `SalesReturn` — read what it is, because this stage deliberately does not duplicate
it), `docs/stages/STAGE-13-warehouse-management.md` (`PickWave`/`PickTask`/`ShipmentConfirmation`, and its
"Notes for later stages" naming this stage as the real caller), ADR-074 (why quotes/invoices are 10c,
not here), ADR-085 (a new caller of an existing concept gets its own reference/event type, not a
duplicate), ADR-087–ADR-091 (the Stage 13 shape this stage builds on), **ADR-092**–**ADR-096** (the
decisions this stage makes).

## Task index

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-14-001 | Verify reopened order-management stage | Stages 10, 13, 08c | NEEDS_VERIFICATION |

## Objective

Stage 09 rings up a till sale. Stage 10 prices it. Stage 13 can shelve and ship stock once something
asks it to. Nothing yet turns "a customer wants this, maybe not from what's on the shelf right now"
into a document the business can track — an order placed at a counter, over the phone, or (once Stage
21 exists) online; a promise that some of it ships today and some of it backorders; a collection instead
of a delivery; a return of what was actually handed over. That document is `SalesOrder`, and this stage
is where it lives.

The central discipline is Stage 13's own instruction, left in `PROGRESS.md` §5 when it shipped:
`PickTask.OutboundReference` is free text specifically so a real sales order id could fill it later
*without a shape change*. This stage is that caller. It does not build a second allocation engine, a
second shipment concept, or a second warehouse demand model — it raises real `PickTask`s against real
`PickWave`s through the unchanged `AddPickTaskCommand` seam, and it reads fulfilment truth back from
`PickTask`/`PickWave` state rather than keeping its own copy.

## What this stage does not own

- **No new stock reservation ledger.** "This order line is promised" is exactly what a Stage 13
  `PickTask.AllocatedQuantity` already means. Building a second reservation concept next to it would
  create two numbers that must always agree and occasionally won't. **ADR-092.**
- **No payment or tender capture.** `SalesOrder.PaymentStatus` is a flag (`Unpaid`, `Paid`, `OnAccount`),
  not a mechanism. Money for an order is taken by Stage 09's till at collection, charged to a Stage 10b
  `CustomerAccount`, or (Stage 21/21b) a future online gateway — this stage names which happened by a
  plain nullable reference (`SettlingSaleId` / `SettlingCustomerAccountId`, neither a cross-schema FK),
  and invents no tender type of its own.
- **No pricing engine.** Every line is priced through Stage 10's `IPriceResolver` at confirm time, and
  the resolution is snapshotted onto the line — the same discipline Stage 10 asked 10c's quotes to
  follow, for the same reason: an expired special must never silently reprice a confirmed order.
- **No quotes, no invoices.** ADR-074 gave those to Stage 10c. A `SalesOrder` has no expiry and this
  stage emits no invoice document — 10c reads `SalesOrder` as a source once it lands, the same
  relationship it has to Stage 09's `Sale`.
- **No wave optimisation, no new pick-allocation strategy.** `IPickAllocationStrategy` and
  `LargestBinFirstAllocationStrategy` are Stage 13's and stay Stage 13's; this stage is a caller.
- **No automatic reallocation on receipt.** This codebase has no event bus — every cross-module reaction
  is an explicit call from a command handler (`IFinancialEventPoster`, `IStockLedgerPoster`, and the
  like), never a subscriber reacting to something it wasn't told about. A backordered line is
  reallocated by `ReattemptBackorderedAllocationsCommand`, called explicitly (by an operator today, by
  a Stage 15 scheduled job later) — not triggered by a Stage 12 goods receipt. Demand-driven automatic
  replenishment is Stage 15's (MRP/DRP) job, not this one's.
- **No carrier or delivery execution.** Stage 24 (Logistics). `ShipmentConfirmation.Carrier`/
  `TrackingNumber` — Stage 13's fields, unchanged — carry free text for a delivery exactly as they do
  for a warehouse-only shipment; click & collect just labels one `Customer Collection`.
- **No real online/marketplace channel.** Stage 21. `Channel` is an enum with values reserved for it;
  the only callers this stage actually exercises are counter and phone orders taken by staff.
- **No approval engine** (rule 13) — nothing here gates on a threshold. If a later requirement wants
  sign-off on a large cancellation or return, that is a Stage 05 `IApprovalService` integration added
  then, not invented here.
- **No GL account, anywhere** (rule 12). This stage raises two new financial event types and chooses no
  account for either.
- **No order screen.** Every deliverable is an endpoint, for the reason Stages 09–13 all stopped at
  (`PROGRESS.md` §4.3, ADR-031): `VumaRetail.Desktop` cannot build or run on this machine.

## Amendments — revision 4 planning arrived after this stage shipped; not built here

Three requirements arrived from `docs/MULTI_COMPANY.md`'s revision-4 planning pass (2026-08-22), on a
branch that diverged from this stage's own build and never merged into it. They describe a **different,
larger redesign** of this stage — `SalesChannel` as a row, an append-only `OrderAllocation`/
`OrderReservation` ledger, `OrderFulfilment`/`OrderReturn` as their own aggregates (ten tables, not the
four above) — none of which is implemented. Nothing above should be read as satisfying them; they are
recorded here so the next session that touches this stage does not have to rediscover them.

1. **COD as a settlement term** (ADR-111) — `SettlementTerms` carrying `CashOnDelivery`, consuming no
   credit-group exposure, released for dispatch only against a captured payment or a named
   driver-collect authorisation.
2. **Delivery geography normalised at capture and snapshotted onto the order** (ADR-113) — `Province`/
   `City`/`Suburb`/`PostalCode` from a per-tenant reference list, so Stage 13b can build consolidated
   pick waves from the snapshot without a later address edit changing what an already-built wave
   contains.
3. **Allocation sourced from Stage 08c's cross-company reservation ledger** (ADR-099, ADR-103) instead
   of this stage's own direct `PickTask` read (ADR-092). **This is the load-bearing one.** ADR-092's own
   text already flags it: "superseded in direction, not yet in code" — the `IOrderFulfilmentReader`
   design above still stands until 08c lands, at which point `ConfirmOrderCommand` and
   `ReattemptBackorderedAllocationsCommand` should be rewired onto `IReservationService`/
   `IAvailabilityService` rather than reading `PickTask` directly, with **available never goes negative
   in any company** applying from the first line of that rework (08c's rule, ADR-103).

None of the three changes the four aggregates or the business rules below — they are a follow-up
rework, tracked here and in `docs/PROGRESS.md`, not a correction of what shipped.

## Deliverables

**`orders` module** (schema `orders`, four tables)

- `SalesOrder` — the aggregate root. `OrderNumber` (new `DocumentNumberCounter` series `ORD`),
  `PartnerId` (nullable `Guid`, no cross-schema FK — a counter/phone order from a walk-in customer is
  legitimate and needs no `Partner` row), `Channel` (`InStore`, `Phone`, `Online`, `Marketplace` — only
  the first two have a real caller this stage), `FulfilmentType` (`Delivery`, `ClickAndCollect`),
  `FulfillingLocationId` (a Stage 08 `StockLocation`, plain `Guid`), `DeliveryAddress` (owned value
  object: line1/line2/city/region/postal code/country — present only when `Delivery`),
  `Status` (`Draft`, `Confirmed`, `PartiallyAllocated`, `Allocated`, `PartiallyFulfilled`, `Fulfilled`,
  `Cancelled`, `Closed`), `PaymentStatus` (`Unpaid`, `Paid`, `OnAccount`), `SettlingSaleId` /
  `SettlingCustomerAccountId` (both nullable, no cross-schema FK), `Currency`, `OrderDate`,
  `RequestedFulfilmentDate`, `IsRevenueRecognised` (guards `orders.order.fulfilled` firing exactly
  once), `CancelledAt`/`CancelledBy`/`CancelReason`. Owns its lines.
- `SalesOrderLine` — item **or** variant (exactly one, Stage 10's `SaleLine` rule), `RequestedQuantity`,
  the priced snapshot (`UnitPrice`, `DiscountAmount`, `TaxAmount`, `PriceListId`, a text summary of the
  promotions that fired — the same shape `PriceResolution` already carries, just persisted),
  `BackorderedQuantity`, `LineStatus` (`Pending`, `Allocated`, `PartiallyAllocated`, `Backordered`,
  `Fulfilled`, `PartiallyFulfilled`, `Cancelled`). **No `AllocatedQuantity`/`FulfilledQuantity` column**
  — both are computed on demand from Stage 13's `PickTask` state keyed by `OutboundReference` (ADR-092);
  persisting a second copy is exactly the drift this stage exists to avoid.
- `SalesOrderReturn` — goods that were actually fulfilled and are coming back. `ReturnNumber` (new
  series `ORT` — deliberately not Stage 10's `RTN`; a `SalesOrderReturn` references a `SalesOrder`, a
  Stage 10 `SalesReturn` references a Stage 09 `Sale`, and the two must never be confused in an audit
  trail, ADR-085's precedent applied to a document series instead of a movement type). `SalesOrderId`,
  `Status` (`Open`, `Completed`), `Reason`, `RefundStatus` (flag, same shape as `PaymentStatus`), who
  authorised it.
- `SalesOrderReturnLine` — one line coming back, referencing the `SalesOrderLine`, a positive
  `Quantity` no greater than that line's fulfilled quantity, and its own net/tax/gross at the line's
  snapshotted price.

**Application layer**

- `ISalesOrderRepository`, `ISalesOrderReturnRepository`.
- `IOrderFulfilmentReader` — the one seam that reads Stage 13 `PickTask`/`PickWave` state back by
  `OutboundReference`: allocated quantity (open tasks' `AllocatedQuantity`), fulfilled quantity (tasks
  on a `Shipped` wave, summed `PickedQuantity`), and the location's current available-to-promise
  (`StockBalance.QuantityOnHand` minus the sum of open tasks' `AllocatedQuantity` for that location+SKU).
  Implemented against Stage 13's existing repositories — an Application-layer cross-module read, the
  same shape Stage 10 already uses against Stage 08's `IStockLocationRepository`.
- Commands: `CreateOrderCommand`, `AddOrderLineCommand`, `ConfirmOrderCommand` (prices every line via
  `IPriceResolver`, then attempts allocation per line through `IOrderFulfilmentReader` + Stage 13's
  unchanged `AddPickTaskCommand`, `OutboundReference` = the line's id), `ReattemptBackorderedAllocationsCommand`
  (oldest order first), `CancelOrderLineCommand`/`CancelOrderCommand` (cancels any open `PickTask` via
  Stage 13's existing cancel command, zeroes lines with none), `RefreshOrderFulfilmentCommand`
  (reconciles `LineStatus` from `IOrderFulfilmentReader`), `CompleteOrderCommand` (refreshes, then — if
  fully accounted for and not already recognised — raises `orders.order.fulfilled` and sets
  `IsRevenueRecognised`), `RecordOrderSettlementCommand` (sets `PaymentStatus` + the settling
  reference), `CreateOrderReturnCommand` + `CompleteOrderReturnCommand` (receives stock through
  `IStockLedgerPoster.ReceiveAsync` at the original shipment's unit cost, `StockReferenceType.OrderReturn`
  — **ADR-093** — and raises `orders.return.completed`; follows ADR-070/ADR-073's precedent: if the
  ledger call is refused, the return still completes).
- Queries: get order (with computed allocation/fulfilment per line), list orders (by status/channel/
  partner/date range), list backordered lines, get order return, list returns for a period.
- `OrdersPermissions` (order.create, order.confirm, order.allocate, order.cancel, order.complete,
  order.return.create, order.return.complete, order.view), `OrdersModuleManifest` (flag `orders`, not
  core — a single-till shop with no counter/phone order-taking is a legitimate deployment).

**API** — `/api/v1/orders/...`, every operation in `/openapi/v1.json` with summaries, error responses
and permission requirements.

**Infrastructure** — four EF configurations, two repositories, `IOrderFulfilmentReader`'s
implementation, a reversible `Orders` migration, DI registration, two new `finance.posting_rules` rows
(`orders.order.fulfilled`, `orders.return.completed`) seeded alongside the module — **new rules, not a
reuse of an existing one**, because order revenue recognition is a genuinely new financial event this
codebase has not raised before (unlike Stage 13's cycle-count variance, which reused Stage 08's).

**Seed** — `scripts/seed.sh` produces a store that has really ordered, really part-shipped, really
backordered and really returned: one order with two lines, one line fully allocated and shipped, the
other deliberately exceeding on-hand so it backorders; a `ReattemptBackorderedAllocationsCommand` run
after a top-up receipt that clears the backorder; a click & collect order collected end to end; and a
return of the first order's shipped line, checked against the ledger and the GL.

## Business rules

1. **Allocation has one source of truth: Stage 13's `PickTask`.** This stage keeps no reservation
   column of its own — a line is "allocated" exactly when an open `PickTask` referencing it exists with
   bins assigned (ADR-092).
2. **Confirming an order allocates whatever fits today and backorders the rest — it never refuses the
   order as a whole.** For each line, `ConfirmOrderCommand` asks `IOrderFulfilmentReader` for
   available-to-promise at the order's `FulfillingLocationId`; whatever fits becomes a `PickTask` via
   the unchanged `AddPickTaskCommand` seam; the remainder becomes `BackorderedQuantity`, no task
   created. This mirrors Stage 13's own rule 7 — "ships what it actually picked" — one layer up.
3. **A backordered line is only reallocated on an explicit call.** No receipt, sale, or any other event
   in this codebase automatically re-triggers allocation — there is no event bus, and building one is
   out of scope (see "What this stage does not own"). `ReattemptBackorderedAllocationsCommand` processes
   every order with `BackorderedQuantity > 0`, oldest `OrderDate` first, so a later order can never
   jump a fairness queue by being reattempted sooner.
4. **Fulfilled quantity is read from Stage 13, never written by this stage.** `RefreshOrderFulfilmentCommand`
   sums `PickedQuantity` across `PickTask`s whose parent `PickWave.Status == Shipped`, keyed by
   `OutboundReference`. A short pick (Stage 13 rule 6) surfaces here as a line that is `PartiallyFulfilled`
   even though its task is `ShortPicked`, not `Backordered` — the difference matters: a short pick is a
   discovered stock discrepancy (Stage 13's problem, resolved by a cycle count), a backorder is demand
   this stage never had stock to promise in the first place.
5. **Revenue is recognised once per order, at `CompleteOrderCommand`, not per partial shipment.** A
   stated v1 simplification: an order that ships in two parts posts one `orders.order.fulfilled` event
   when it is completed, guarded by `IsRevenueRecognised` so a second `CompleteOrderCommand` call is a
   no-op rather than a double-post. A future stage that needs per-shipment recognition changes this
   rule explicitly; it is not assumed here.
6. **A `SalesOrderReturn` only ever returns quantity that was actually fulfilled.** Unfulfilled quantity
   — allocated or not — is cancelled, never "returned". This keeps a clean line between Stage 10's
   `SalesReturn` (returns a completed till `Sale`) and this stage's `SalesOrderReturn` (returns a
   fulfilled order line that was never rung up as a `Sale` at all) — the two are never the same document
   and neither stage reads the other's table.
7. **Cancelling a line with an open `PickTask` cancels the task through Stage 13's existing command
   first.** A line with no task (still `Pending`, or already `Backordered`) is simply zeroed — there is
   nothing downstream to unwind.
8. **Click & collect and delivery share one fulfilment pipeline.** Both raise `PickTask`s the same way
   and both complete through Stage 13's `ShipmentConfirmation` — the only difference is
   `CollectionLocationId` (= `FulfillingLocationId`) versus `DeliveryAddress`, and the label written into
   `ShipmentConfirmation.Carrier` (`Customer Collection` for the former). Neither this stage nor Stage 13
   grows a second "goods left the building" concept.
9. **A confirmed order's line price is immutable.** `IPriceResolver` runs once, at confirm time, and the
   resolution is snapshotted onto the line; a price list or promotion changing afterward never reprices
   a confirmed order (the same discipline Stage 10 asked of Stage 10c's quotes).
10. **`PartnerId` is nullable.** A counter or phone order taken from someone with no `Partner` record is
    a legitimate sale this stage must not block.
11. **Everything is soft-deleted and tenant-scoped** (§7 rules 3 and 8), and no table in this schema
    draws a foreign key into another schema — `PartnerId`, `FulfillingLocationId`, `SettlingSaleId` and
    `SettlingCustomerAccountId` are all plain `Guid` (`CONVENTIONS.md` §2).
12. **No module names a GL account** (rule 12) — see "What this stage does not own".

## Tests / acceptance

**Unit** (Domain + Application, ≥ 80% line coverage on the stage's new code)

- `SalesOrder`/`SalesOrderLine` construction, line pricing snapshot, status transitions.
- `ConfirmOrderCommand`: full allocation (fits entirely), partial allocation (splits into allocated +
  backordered on the same line), full backorder (nothing fits), and the resulting `PickTask`(s) really
  carry `OutboundReference` = the line's id — asserted against a fake `IOrderFulfilmentReader`/Stage 13
  port, not a real database.
- `ReattemptBackorderedAllocationsCommand`: oldest-order-first ordering with two competing backordered
  orders and only enough stock for one.
- `RefreshOrderFulfilmentCommand`: full ship, partial ship, short pick maps to `PartiallyFulfilled` not
  `Backordered` (rule 4's distinction, with a worked example).
- `CompleteOrderCommand`: raises the financial event exactly once across two calls (idempotency guard).
- `CancelOrderLineCommand`: with an open task (delegates to Stage 13's cancel), without one (zeroes the
  line), refuses cancelling an already-fulfilled quantity.
- `CreateOrderReturnCommand`/`CompleteOrderReturnCommand`: refuses a quantity greater than fulfilled,
  posts at the original shipment's unit cost, still completes when the ledger call is refused
  (ADR-070/ADR-073 precedent).

**Integration** (Testcontainers Postgres, real HTTP)

- The whole chain end to end: an order with two lines against a location with stock for one → confirm →
  one line's `PickTask` created and the other `Backordered` → pick/pack/ship that task through Stage
  13's existing endpoints → `RefreshOrderFulfilmentCommand` shows it `Fulfilled` → a receipt tops up the
  short SKU → `ReattemptBackorderedAllocationsCommand` allocates the second line → shipped → `CompleteOrderCommand`
  posts `orders.order.fulfilled` once, checked in the GL.
- A click & collect order, collected end to end, `ShipmentConfirmation.Carrier == "Customer Collection"`.
- A return of the first order's shipped line: stock really returns to `StockBalance` at the original
  cost, `orders.return.completed` reaches the GL.
- The migration applies and reverses (`Down` tested).
- Every endpoint appears in `/openapi/v1.json`.
- Permission enforcement on confirm, cancel, complete, and return-complete.

**Architecture**

- `LayeringTests` sees the new code; no `orders` type names a GL account; `VumaRetail.Application.Orders`
  references Stage 13's Application-layer ports, never the other way — a compile-time check that
  Stage 13 was not made to depend on Stage 14.
- `ReplicationRulesTests` sees every new replicated entity (`SalesOrder`, `SalesOrderLine`,
  `SalesOrderReturn`, `SalesOrderReturnLine` — all four, unlike `BinStock`/`StockBalance`, are not
  node-local projections; they are the order itself and must sync).
- `CommandClassificationTests` sees every new command (ADR-078's manifest-derived sweep).
- No cross-schema foreign key.

## Exit checklist

- [x] `dotnet build -c Release` — zero warnings, zero errors
- [x] `dotnet test` — all green via `scripts/test.sh`; ≥ 80% line coverage on the stage's Domain +
      Application (91.3% achieved)
- [x] `Orders` migration generated, applied and reversed (`Down` tested), snapshot not stale
- [x] Every endpoint in `/openapi/v1.json` with summaries and error responses
- [x] Permissions registered in the RBAC catalogue; confirm/cancel/complete/return-complete each
      asserted to 403 without them, one test case per operation
- [x] `orders.order.fulfilled` and `orders.return.completed` posting rules registered with Stage 07 and
      **verified rather than assumed** — a seeded order really reaches the GL through them
- [x] Module entitlement flag declared in the manifest (`orders`, not core)
- [x] Usage counters — automatic, via the `orders` schema (`UsageCounterSource` groups by schema)
- [x] Replication scope declared on all four new entities; `SYNC_AND_BACKUP.md` and `DATA_MODEL.md`
      updated
- [x] `scripts/seed.sh` produces a store that has really ordered, part-shipped, backordered, reallocated,
      collected and returned — checked in SQL and against the GL
- [x] ADR-092–ADR-096 appended to `docs/DECISIONS.md`
- [ ] The six agent reviews (`docs/AGENTS.md`) run at the checkpoints CLAUDE.md specifies; findings acted
      on or recorded with a reason. **This is the oldest standing debt in the project** (`PROGRESS.md`
      §4.17) — this stage did not add a sixth entry to that list because the environment this session
      ran in could not spawn subagents; it remains open and is tracked in `PROGRESS.md` alongside every
      other stage still owed a real review.
- [x] `docs/PROGRESS.md` updated, committed

## Notes for later stages

- **Stage 10c (quotes, invoices, sales analytics)** reads `SalesOrder` as one of its sources, the same
  relationship it has to Stage 09's `Sale`. Nothing here should need to change shape to support it.
- **Stage 15 (replenishment/MRP)** is the natural home for turning `ReattemptBackorderedAllocationsCommand`
  from an explicit call into a scheduled reaction to receipts — this stage deliberately does not build
  that trigger.
- **Stage 21/21b (ecommerce, Vuma Connect)** become `Channel.Online`/`Marketplace`'s real callers;
  `CreateOrderCommand` should not need to change shape to accept them, only who calls it.
- **Stage 24 (logistics)** is where `ShipmentConfirmation.Carrier`/`TrackingNumber` — still Stage 13's
  fields — graduate into a real carrier integration; this stage adds no carrier logic of its own.
- **Per-shipment revenue recognition**, if a later requirement needs it, is a deliberate change to rule
  5, not an oversight being corrected.
- **Stage 06c/08c (multi-company, cross-company availability)** are this stage's largest open follow-up
  — see "Amendments — revision 4 planning" above and ADR-092's supersession note. Allocation moving from
  a direct `PickTask` read to Stage 08c's `IReservationService`/`IAvailabilityService` is a rework, not
  a greenfield build; do it once 06c/06d/08c exist rather than guessing their shape now.
