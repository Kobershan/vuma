# STAGE 14 — Order Management: omnichannel orders, allocation, backorders, click & collect, returns

**Status:** IN_PROGRESS (started 2026-08-19) · **Depends on:** 10 (price lists, promotions, the
return document this stage does *not* rebuild), 13 (the pick wave this stage becomes the real demand
source for), 08 (the stock ledger every quantity fact still resolves to), 06 (items, variants,
customers), 07 (the posting rules engine the fulfilment event reaches) ·
**Reference reading:** `docs/DATA_MODEL.md` §1–§3, `docs/CONVENTIONS.md` §1–§6,
`docs/API_STANDARDS.md` §3–§5 and §8–§9, `docs/TESTING.md` §2–§3, `CLAUDE.md` §7 rules 2, 3, 4, 5, 6,
7, 8, 9, 12, 13, ADR-005 (the append-only ledger), ADR-065 (document number sequences), ADR-068
(weighted-average cost), ADR-072 (`IPriceResolver`, the port this stage prices through), ADR-075 (tax
is stored on the line, never re-derived), ADR-081 (a commitment document posts nothing to the GL),
ADR-085 (a new caller of an existing movement type gets its own reference type), ADR-087 (the bin id
on `StockLedgerEntry`), **ADR-092**–**ADR-098** (the decisions this stage makes).

## Objective

Stage 09 sells to somebody standing at a till. Stage 10 works out what the price should be and takes
goods back over that same counter. Stage 13 can pick, pack and ship a wave of demand — from a list of
lines a caller hands it, because the caller that should raise them did not exist yet. This stage is
that caller, and it is the first place in the build where a customer can want something the shop is
not currently holding in front of them.

An order is a **promise**, and this stage is about the arithmetic of keeping it: what has been
promised, what is reserved against it, what is still owed, and where the goods physically go on their
way to the customer. One order shape serves every channel — a website, a phone call, a marketplace
feed, a salesperson at a counter — because the difference between them is *where the order came from*
and *how the goods get to the customer*, not what an order is.

## What this stage does not own

- **No storefront, no channel connector, no EDI.** `SalesChannel` is a configured row saying "orders
  arrive here, priced off this list, fulfilled from this location by default". The API that a real
  website or marketplace posts an order into is Stage 21's, and its DTOs are Stage 21's problem
  (rule 14). This stage builds the order the connector will create, and an endpoint that creates one.
- **No payment capture, no AR invoice.** An order records what it is worth and what has been settled
  against it as a **reference**, exactly as Stage 12's purchase order records committed spend without
  posting it (ADR-081). Money moves when the till tenders (Stage 09), when Finance posts an AR
  invoice (Stage 07), or when Stage 21b's in-app payment lands — never here. **ADR-093.**
- **No second return document.** Stage 10's `SalesReturn` is the counter return: a customer with a
  receipt, at a till, refunded on the spot. An order return is a different physical event — goods
  posted back to a warehouse days later, authorised before they travel — so this stage builds the
  *authorisation and receipt* half (`OrderReturn`) and reuses Stage 10's ledger and financial-event
  shape for the money and the stock. It does not restate, wrap or deprecate `SalesReturn`. **ADR-097.**
- **No approval engine** (rule 13). Nothing here gates on a threshold. A credit-limit hold on a
  confirmed order is exactly the kind of gate Stage 05 exists for, and is left for it — this stage
  records `CreditHold` as an order status a command can set and clear, with no policy behind it.
- **No GL account, anywhere** (rule 12). Fulfilment and refund raise `OrderFulfilledEvent` and
  `OrderReturnRefundedEvent`; which accounts they hit is a posting rule a tenant owns (ADR-016).
- **No delivery routing, no carrier integration.** Stage 24. A fulfilment carries the same free-text
  `Carrier`/`TrackingNumber` Stage 13's `ShipmentConfirmation` does, and for the same reason.
- **No demand forecasting, no automatic replenishment of a backorder.** Stage 15. A backordered line
  waits in a queue this stage can query and re-allocate on command; nothing here raises a purchase
  requisition to cover it.
- **No order screen.** Every deliverable is an endpoint, for the reason Stages 09–13 all stopped at
  (`PROGRESS.md` §4.3, ADR-031): `VumaRetail.Desktop` cannot build or run on this machine.


## Amendments — revision 4, 2026-08-22

Three requirements arrived after this document was written. They are additive; nothing already
specified above is withdrawn. Read `docs/MULTI_COMPANY.md` §4–§5 and ADR-103, ADR-111, ADR-113 first.

1. **COD is a settlement term on the order** (ADR-111). `SettlementTerms` carries `CashOnDelivery`,
   inherited by every invoice the order produces and printed on the delivery note. A COD order
   **consumes no credit-group exposure**, and it cannot be released for dispatch without either a
   captured payment or an explicit driver-collect authorisation naming who collects. The outstanding
   COD amount reconciles against the delivery run when the driver returns.
2. **Delivery geography is normalised at capture and snapshotted onto the order** (ADR-113) —
   `Province`, `City`, `Suburb`, `PostalCode`, from a per-tenant reference list. Stage 13b builds
   consolidated pick waves from this snapshot, and a later address edit must not change what a built
   wave contains. This is three columns and a normaliser; leaving it to 13b means back-filling
   geography onto orders that already exist.
3. **Allocation reserves through Stage 08c's reservation ledger, which lives in each company's own
   database** (ADR-099, ADR-103).
   `OrderAllocation`/`OrderReservation` above predate 08c. Where 08c has landed, this stage consumes
   `IReservationService` and `IAvailabilityService` rather than computing available-to-promise itself;
   where it has not, build the local model behind those two interface names so 08c replaces the
   implementation and no caller changes. **Available never goes negative in any company** is 08c's rule
   and it applies here from the first line of allocation code. An order sourced from two companies takes
   two reservations in two databases, coordinated as a saga — this stage never opens a transaction
   against more than one company (ADR-116).

Also inherited, from stages either side of this one: order lines carry a **pack size** snapshot
(ADR-112, printed by 10c), and an order sourced across companies produces **one invoice per supplying
company** (ADR-102). Neither changes the order aggregate — both are line-level snapshot fields and a
document-splitting step downstream.

## Deliverables

**`orders` module** (schema `orders`, ten tables)

*Channel configuration*

- `SalesChannel` — where orders come from. `Code`, `Name`, `ChannelType` (`InStore`, `Online`,
  `Phone`, `Marketplace`, `Wholesale`), the default `PriceListId` orders on this channel price
  against, the default `FulfilmentLocationId`, `IsActive`. A row, not an enum, so adding a
  marketplace is configuration (**ADR-092**).

*The order*

- `SalesOrder` — the promise. `OrderNumber` (ADR-065, series `SO`), `ChannelId`, `CustomerId`
  (optional — a guest order is legitimate), `StoreId`, `FulfilmentLocationId`, `Currency`,
  `FulfilmentMethod` (`Delivery`, `ClickAndCollect`, `CounterCollect`), `Status` (`Draft`,
  `Confirmed`, `CreditHold`, `Allocated`, `PartiallyAllocated`, `Fulfilling`, `Fulfilled`,
  `Cancelled`), delivery address as owned free text, `RequestedDate`, `Net`/`Tax`/`Gross`, a
  free-text `SettlementReference` (ADR-093), and the who/when of each transition. Owns its lines.
- `SalesOrderLine` — item/variant, `OrderedQuantity`, `UnitPrice` and `DiscountAmount` (resolved
  through Stage 10's `IPriceResolver` at confirm, then frozen — ADR-075's rule applied to an order),
  `Net`/`Tax`/`Gross`, `AllocatedQuantity`, `FulfilledQuantity`, `CancelledQuantity`,
  `ReturnedQuantity`, `Status` (`Pending`, `Allocated`, `PartiallyAllocated`, `Backordered`,
  `Fulfilled`, `Cancelled`).

*Allocation and backorders*

- `OrderAllocation` — the append-only record of quantity being reserved against, or released from, an
  order line at a location. `IImmutableRecord`, signed `Quantity`, an `OrderAllocationType`
  (`Reserve`, `Release`, `Consume`, `Cancel`), the line and the location. The same shape
  `BinStockMovement` has one level down, and for the same reason: a reservation that can be edited is
  a reservation nobody can audit. **ADR-094.**
- `OrderReservation` — the projection, one row per location per stock-keeping unit,
  `ReservedQuantity` only. Node-local, not replicated, maintained transactionally alongside every
  `OrderAllocation` insert — `StockBalance`'s relationship to `StockLedgerEntry`, exactly.
  **Available-to-promise** is `StockBalance.QuantityOnHand − OrderReservation.ReservedQuantity`, and
  it is the only number allocation is allowed to spend. **ADR-095.**
- **A backorder is not a table.** It is a line whose `OrderedQuantity` exceeds its
  `AllocatedQuantity + CancelledQuantity`, queryable oldest-first. **ADR-096.**

*Fulfilment*

- `OrderFulfilment` — one physical dispatch or collection. `SalesOrderId`, `Method`, `Status`
  (`Planned`, `Picking`, `ReadyForCollection`, `Dispatched`, `Collected`, `Cancelled`), the Stage 13
  `PickWaveId` when one was raised, `CollectionCode` (the click & collect PIN), `CollectedByName`,
  free-text `Carrier`/`TrackingNumber`, and the who/when of each transition. Owns its lines. An order
  can have several — a partial dispatch now and the backorder next week is the normal case.
- `OrderFulfilmentLine` — an order line and how much of it this fulfilment carries.

*Returns*

- `OrderReturn` — the authorisation. `ReturnNumber` (ADR-065, series `ORT`), `SalesOrderId`,
  `Reason`, `Status` (`Requested`, `Authorised`, `Received`, `Refunded`, `Rejected`, `Cancelled`),
  `ReceivedLocationId`, `Net`/`Tax`/`Gross`, and the who/when of each transition. Owns its lines.
- `OrderReturnLine` — the order line coming back, `Quantity`, `PreviouslyReturnedQuantity` (the
  cumulative guard Stage 10's `SalesReturnLine` uses, same shape and same check constraint),
  `UnitPrice` copied off the order line, tax derived pro-rata from the order line's stored tax
  (ADR-075), and a `Restock` flag — damaged goods come back to the customer's credit but not to
  saleable stock.

**Application layer**

- `ISalesChannelRepository`, `ISalesOrderRepository`, `IOrderAllocationRepository`,
  `IOrderReservationRepository`, `IOrderFulfilmentRepository`, `IOrderReturnRepository`.
- `IAvailableToPromiseService` — on-hand less reserved, the one place ATP is computed.
- `IOrderAllocator` — given an order, reserves what it can and leaves the rest backordered. One
  implementation, `AvailableToPromiseAllocator`.
- `IOutboundDemandPlanner` (in `Application.Abstractions.Warehouse`, implemented in
  `Application/Warehouse`) — the published port Stage 13's pick wave is raised through, so the orders
  module depends on a contract rather than on the warehouse module's internals. The
  `IPriceResolver` / `ITaxCalculator` pattern.
- `IOrderFinancialEventPublisher` + `OrderFulfilledEvent` / `OrderReturnRefundedEvent`.
- `OrderFulfilmentCompletionService` — the one place a dispatch or collection freezes the fulfilment,
  consumes the reservation, issues the stock (or records that Stage 13 already did) and raises the
  financial event. `SaleCompletionService`'s shape.
- ~22 commands, ~12 queries, `OrderPermissions` (10 permissions), `OrdersModuleManifest` (flag
  `orders`, not core — a counter-only shop that never promises anything it is not holding is a
  legitimate deployment).

**API** — `/api/v1/orders/...`, every operation in `/openapi/v1.json` with summaries, error
responses and permission requirements.

**Infrastructure** — ten EF configurations, six repositories, a reversible `Orders` migration, DI
registration, the financial event publisher.

**Seed** — `scripts/seed.sh` produces a shop that has really promised and really delivered: two
channels; an online order priced off the channel's list, confirmed, allocated, picked through a real
Stage 13 wave and dispatched (stock really left, checked against the ledger); a click & collect order
collected against its PIN; a third order with a line that could not be allocated, left backordered,
then released after a receipt; and a return authorised, received and refunded.

## Business rules

1. **An order posts nothing to the general ledger until goods move** (ADR-081's precedent, ADR-093).
   Confirming an order reserves stock and owes a customer something; it does not recognise revenue.
2. **Prices are resolved once, at confirm, and then frozen.** A draft order re-prices on every change;
   a confirmed one never re-prices. A promotion ending tomorrow does not restate what a customer was
   promised today (ADR-075's rule, applied to an order rather than a receipt).
3. **Nothing is allocated beyond available-to-promise** — on-hand less what other orders already hold.
   Allocation is the only consumer of that number, and it is computed in one place (ADR-095).
4. **Allocation does not move stock.** It reserves it. `StockBalance.QuantityOnHand` is unchanged by
   an allocation; what changes is how much of it is spoken for. Stock leaves at fulfilment.
5. **Stock leaves exactly once, and whoever owns the physical moment posts it** (**ADR-098**). A
   `Delivery` fulfilment raises a Stage 13 pick wave and Stage 13's `ShipmentConfirmation` posts the
   ledger issue; the orders module then records the dispatch and consumes the reservation without
   posting again. A `ClickAndCollect` or `CounterCollect` fulfilment has no wave, so the orders module
   posts the issue itself through a new `IStockLedgerPoster.IssueForOrderFulfilmentAsync` with its own
   reference type (ADR-085's precedent). Double-posting is the failure this rule exists to prevent, and
   there is a test for it.
6. **A partial fulfilment is normal, not an error.** An order ships what it has and keeps owing the
   rest; the line's `FulfilledQuantity` and the backorder queue both reflect it.
7. **A backordered line holds no reservation.** It is unallocated demand, and it is served
   oldest-order-first when `AllocateBackordersCommand` runs against fresh stock (ADR-096). Fairness is
   by order date, not by order size.
8. **Cancelling a line releases its reservation, and cancelling a fulfilled quantity is refused.**
   What has left the building comes back through a return, never through a cancellation.
9. **A click & collect order is not collected without its code.** `ConfirmCollectionCommand` matches
   the `CollectionCode` and refuses on a mismatch — the one place this stage guards against giving
   goods to the wrong person.
10. **Nothing comes back that did not go out.** An `OrderReturnLine`'s quantity plus everything
    previously returned against that order line may not exceed the line's `FulfilledQuantity` —
    enforced in the aggregate and as a check constraint, exactly as Stage 10 enforces it.
11. **A non-restock return credits the customer and receives no stock.** The financial event fires;
    `IStockLedgerPoster` is not called.
12. **Everything is soft-deleted and tenant-scoped** (§7 rules 3 and 8), and no table in this schema
    draws a foreign key into another schema.

## Parts — the build list

Ticked as each lands. This is the working checklist for the stage, not a summary written afterwards.

**A. Groundwork**
- [ ] A1 — Land Stage 13: run the full suite, commit the pending work, merge `stage-13-warehouse-management` to `main`
- [ ] A2 — Branch `stage-14-order-management` off `main`
- [ ] A3 — This stage document, `ROADMAP.md`/`PROGRESS.md` status flipped to IN_PROGRESS

**B. Domain (`src/VumaRetail.Domain/Orders`)**
- [ ] B1 — `OrderEnums.cs`, `OrderExceptions.cs`, `OrderItemReference.cs`
- [ ] B2 — `SalesChannel`
- [ ] B3 — `SalesOrder` + `SalesOrderLine` (the aggregate, its transitions, its totals)
- [ ] B4 — `OrderAllocation` + `OrderReservation`
- [ ] B5 — `OrderFulfilment` + `OrderFulfilmentLine`
- [ ] B6 — `OrderReturn` + `OrderReturnLine`

**C. Application (`src/VumaRetail.Application/Orders`)**
- [ ] C1 — `OrderPorts.cs` (six repositories), `OrdersActor`
- [ ] C2 — `AvailableToPromiseService`, `OrderAllocator`
- [ ] C3 — `IOutboundDemandPlanner` port + its `Application/Warehouse` implementation
- [ ] C4 — `IStockLedgerPoster.IssueForOrderFulfilmentAsync` + `ReceiveForOrderReturnAsync` (additive, ADR-098/ADR-085)
- [ ] C5 — `OrderFulfilmentCompletionService` + the financial events and publisher port
- [ ] C6 — Commands: channel, order lifecycle, allocation, fulfilment, collection, return
- [ ] C7 — Queries: order, orders page, ATP, backorder queue, fulfilments, collection lookup, returns
- [ ] C8 — `OrderPermissions` + `OrdersModuleManifest`

**D. Infrastructure**
- [ ] D1 — `Schemas.Orders`, ten EF configurations
- [ ] D2 — Six repositories + the financial event publisher
- [ ] D3 — DI registration (`OrdersServiceCollectionExtensions`)
- [ ] D4 — `Orders` migration, generated and reversed

**E. API**
- [ ] E1 — `src/VumaRetail.Web/Orders/OrderEndpoints.cs`, all operations, permissions, OpenAPI

**F. Tests**
- [ ] F1 — Unit: domain aggregates and transitions
- [ ] F2 — Unit: ATP arithmetic, the allocator, backorder fairness
- [ ] F3 — Unit: the stage's application services (completion, returns, pricing freeze)
- [ ] F4 — Integration: the whole chain (order → allocate → wave → ship → dispatch)
- [ ] F5 — Integration: click & collect, backorder release, return, migration up/down, OpenAPI, permissions
- [ ] F6 — Architecture: layering, replication, command classification, no cross-schema FK, no GL account

**G. Wiring and close-out**
- [ ] G1 — Seed (`scripts/seed.sh`) and its SQL verification
- [ ] G2 — Posting rules seeded for `orders.fulfilment` / `orders.return.refunded`
- [ ] G3 — `SYNC_AND_BACKUP.md` + `DATA_MODEL.md` replication registry updated
- [ ] G4 — ADR-092 – ADR-098 appended to `docs/DECISIONS.md`
- [ ] G5 — Exit checklist executed, `PROGRESS.md` updated
- [ ] G6 — Merge to `main`, push, ecosystem submodule pointer advanced

## Tests / acceptance

**Unit** (Domain + Application, ≥ 80% line coverage on the stage's new code)

- `SalesOrder`: draft add/remove line, confirm freezes prices and totals, refuse editing a confirmed
  order, cancel releases, refuse cancelling a fulfilled quantity, totals equal the sum of lines.
- `SalesOrderLine`: allocate/partially allocate/backorder status transitions, fulfil, over-fulfil
  refused, return quantity guard.
- `OrderReservation`: reserve/release arithmetic, refusing a negative result, refusing a UoM mismatch.
- `AvailableToPromiseService`: on-hand less reserved, never negative, unknown SKU is zero not a throw.
- `OrderAllocator`: exact fit, partial fit leaving a backorder, nothing available at all, and the
  fairness rule — two orders competing for one unit, the older one wins.
- `OrderFulfilment`: plan, dispatch, collect with the right code, refuse the wrong code, refuse
  collecting a dispatched fulfilment, refuse a fulfilment line beyond what is allocated.
- `OrderReturn`: authorise, receive, refund, cumulative quantity guard, non-restock skips the ledger.
- **The double-post guard**: a `Delivery` fulfilment whose wave already shipped does not call
  `IStockLedgerPoster` again; a `ClickAndCollect` fulfilment does. One test each (ADR-098).
- The extended `IStockLedgerPoster`: the two new methods write their own `StockReferenceType` and no
  existing call site changes behaviour.

**Integration** (Postgres, real HTTP)

- The whole chain: channel → customer → priced items with stock → order confirmed → allocated (ATP
  really fell) → fulfilment raises a real Stage 13 pick wave → pick, pack, ship → dispatch recorded →
  `StockBalance` really decreased exactly once, and the order reads `Fulfilled`.
- Click & collect: order → allocate → fulfilment `ReadyForCollection` → wrong code refused → right
  code collects → stock issued by the orders module itself.
- Backorder: an order for more than is on hand → the excess is backordered and holds no reservation →
  a Stage 08 receipt lands → `AllocateBackordersCommand` → the older order is served first.
- A return authorised, received with restock, refunded — stock back in, financial event raised — and a
  second return that would exceed what was fulfilled, refused.
- The migration applies and reverses (`Down` tested).
- Every endpoint appears in `/openapi/v1.json`.
- Permission enforcement on order confirm, allocate, dispatch, collect and return refund — one case
  each, not one standing in for the group.

**Architecture**

- `LayeringTests` sees the new code; no orders type names a GL account.
- `ReplicationRulesTests` sees every new replicated entity.
- `CommandClassificationTests` sees every new command (manifest-derived sweep, ADR-078).
- No cross-schema foreign key.

## Exit checklist

- [ ] `dotnet build -c Release` — zero warnings, zero errors
- [ ] `dotnet test` — all green via `scripts/test.sh`, ≥ 80% line coverage on the stage's Domain +
      Application
- [ ] `Orders` migration generated, applied and reversed
- [ ] Every endpoint in `/openapi/v1.json` with summaries and error responses
- [ ] Permissions registered in the RBAC catalogue (`IModulePermissions`)
- [ ] Financial posting rules registered for `orders.fulfilment` and `orders.return.refunded`, and
      verified by a seeded database really producing balanced journals
- [ ] Module entitlement flag declared in the manifest — `OrdersModuleManifest`, flag `orders`, not core
- [ ] Usage counters — automatic, via the `orders` schema
- [ ] Replication scope declared on every new entity; `SYNC_AND_BACKUP.md` and `DATA_MODEL.md` updated
- [ ] `scripts/seed.sh` produces an order that has really been promised, allocated, shipped and returned
- [ ] ADR-092 – ADR-098 appended to `docs/DECISIONS.md`
- [ ] The six agent reviews run, findings acted on or recorded
- [ ] `docs/PROGRESS.md` updated, committed

## Notes for later stages

- **Stage 15 (Replenishment/MRP)** reads the backorder queue as its demand signal — unallocated order
  demand is exactly what a replenishment run should be covering, and it is a query, not a table, so
  nothing needs restating.
- **Stage 21 (Ecommerce)** is `SalesChannel`'s real caller. The channel row exists now specifically so
  a storefront becomes configuration plus its own DTOs, not a new order model.
- **Stage 23 (Service)** takes `OrderReturn` further into repair/RMA workflow; this stage stops at
  authorise → receive → refund.
- **Stage 24 (Logistics)** graduates `Carrier`/`TrackingNumber` from free text into a real carrier
  integration, as it does for Stage 13's `ShipmentConfirmation`.
- **Credit holds** are a status with no policy behind it, waiting for Stage 05's `IApprovalService`.
