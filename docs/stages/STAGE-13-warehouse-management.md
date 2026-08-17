# STAGE 13 — Warehouse Management: zones, bins, putaway, pick/pack/ship, cycle counts

**Status:** IN_PROGRESS · **Depends on:** 08 (stock ledger — a bin is a subdivision of a Stage 08
`StockLocation`, never a parallel quantity system) · **Reference reading:** `docs/DATA_MODEL.md` §1–§3,
`docs/CONVENTIONS.md` §1–§6, `docs/API_STANDARDS.md` §3–§5 and §8–§9, `docs/TESTING.md` §2–§3,
`CLAUDE.md` §7 rules 2, 3, 4, 5, 6, 8, 9, 12, 13, ADR-005 (the append-only ledger this stage extends,
never replaces), ADR-068 (weighted-average cost — unchanged by this stage), ADR-085 (a new caller of an
existing movement type gets its own reference type, not a new movement type), **ADR-087**–**ADR-091**
(the decisions this stage makes).

## Objective

Stage 08 said what is on hand and what it is worth, at the granularity of one `StockLocation` — a
warehouse, a back room, a sales floor. It deliberately stopped there: "no bins, zones, putaway or
picking — that is Stage 13. A location is the smallest unit stock is tracked at; Stage 13 subdivides
it." This stage is that subdivision: it answers *where inside the location*, and the four physical
motions that follow from knowing — put it away, count it, pick it, ship it.

Every quantity fact this stage owns is additive to Stage 08, never a competing source of truth. A
location's total on-hand quantity is still `StockBalance.QuantityOnHand`, summed from
`StockLedgerEntry` rows exactly as Stage 08 built it. This stage adds a nullable `BinId` to that same
ledger entry (ADR-087) so a movement that happened at bin granularity says which bin, and adds its own
projection — `BinStock` — for "how much of this SKU is in this specific bin", built from an
append-only `BinStockMovement` ledger that mirrors `StockLedgerEntry`'s shape at one level down. A bin
reassignment that does not change what a location holds (putaway, an internal bin-to-bin move) posts
only to `BinStockMovement`; a movement that changes what the location holds (a shipment leaving, a
cycle count variance) posts through the extended `IStockLedgerPoster` — one call, both projections kept
consistent, because both are maintained by the same transaction.

## What this stage does not own

- **No valuation, no weighted-average arithmetic.** `BinStock` carries quantity only. Cost lives at the
  location level in Stage 08's `StockBalance`, unchanged — a bin move never changes what a location's
  stock is worth, only where inside it the physical units sit.
- **No sales order, no demand planning.** Order Management is Stage 14 and does not exist yet. A
  `PickWave`'s lines are supplied directly by the caller (an item/variant, a quantity, and a free-text
  `OutboundReference`) — the same deferral shape Stage 12 used for `PurchaseRequisition` before Stage 15
  exists to raise one automatically. When Stage 14 lands, it becomes the wave's real caller; nothing
  about `PickWave`'s shape needs to change to receive demand from a real sales order instead of a
  caller-supplied line.
- **No GL account, anywhere** (rule 12). A cycle count variance raises the same
  `InventoryValuationEvent` a Stage 08 stocktake does, through the same `IStockLedgerPoster`, so it
  reaches the general ledger by the same seeded `inventory.stocktake.shortage` /
  `inventory.stocktake.surplus` rules Stage 08 already registered. This stage names no new event type
  and adds no posting rule of its own — the variance a cycle count finds is economically identical to
  the variance a full stocktake finds; only the counting workflow differs.
- **No approval engine** (rule 13). Nothing in this stage gates on a threshold — a cycle count variance
  posts on finalize the same way a Stage 08 stocktake does, with no approval step in either. If a later
  stage wants one (a large-variance cycle count requiring sign-off, say), it is a Stage 05
  `IApprovalService` integration added then, not invented here.
- **No handheld device driver.** R3 makes this moot by construction: "handheld" means the API surface a
  scanner would drive — scan a bin barcode, look up what should be there, confirm a quantity — and that
  surface is exactly the REST endpoints below. No hardware SDK, no serial port, no Windows service
  changes.
- **No warehouse screen.** Every deliverable is an endpoint, for the reason Stages 09–12 all stopped at
  (`PROGRESS.md` §4.3, ADR-031): `VumaRetail.Desktop` cannot build or run on this machine.
- **No cross-warehouse replenishment, no wave-optimisation algorithm, no slotting engine.** A pick wave
  in this stage is a flat list of demand lines a caller supplies and the system allocates bins for by a
  simple "largest available bin-stock first" rule (ADR-090) — a working, testable allocator, not a
  claim that it is the optimal one. A later stage can replace the allocator behind
  `IPickAllocationStrategy` without touching anything else.

## Deliverables

**`warehouse` module** (schema `warehouse`, eleven tables)

*Location structure*

- `Zone` — a named subdivision of a Stage 08 `StockLocation` (`LocationId`, plain `Guid`, never a
  cross-schema foreign key — `CONVENTIONS.md` §2). `Code`, `Name`, `ZoneType` (`Receiving`, `Storage`,
  `Picking`, `Packing`, `Shipping`, `Returns`).
- `Bin` — a subdivision of a `Zone`. `Code` (unique within the location), `Name`, `BinType` (`Shelf`,
  `Pallet`, `Bulk`, `Staging`, `Dock`), optional `Capacity` (a plain `Quantity`, informational — nothing
  in this stage refuses a putaway for exceeding it, see business rule 9), `IsActive`.

*Bin-level stock*

- `BinStockMovement` — the append-only record of stock moving into, out of, or between bins, at the
  same granularity `StockLedgerEntry` records at location level. `IImmutableRecord`, signed `Quantity`,
  a `BinStockMovementType` (`PutawayIn`, `PutawayOut`, `PickReserve`, `PickRelease`,
  `InternalTransferIn`, `InternalTransferOut`), and a reference back to the task that caused it
  (`PutawayTask`, `PickTask`, or a bin-to-bin transfer's own shared id). **ADR-088.**
- `BinStock` — the projection, one row per bin per stock-keeping unit, quantity only. Maintained
  transactionally alongside every `BinStockMovement` insert and alongside every bin-tagged
  `StockLedgerEntry` this module posts, the same relationship `StockBalance` has to `StockLedgerEntry`.
  Node-local, not replicated — the same reasoning ADR-069 gives for `StockBalance`.

*Putaway*

- `PutawayTask` — one line of unbinned received stock to be shelved. `LocationId`, item/variant,
  `Quantity`, the receiving document it came from (`SourceReferenceType` + `SourceReferenceId` — a
  Stage 12 `GoodsReceiptLine`, or a Stage 08 manual receipt/adjustment), a `SuggestedBinId` the
  allocator proposes, `Status` (`Pending`, `Confirmed`, `Cancelled`), the `ConfirmedBinId` and
  `ConfirmedQuantity` actually shelved (may differ from suggested — the picker scanned a different bin
  or split the quantity across two), and the who/when of confirmation.

*Pick, pack, ship*

- `PickWave` — a batch of outbound demand to fulfil from one location. `LocationId`, `Status` (`Open`,
  `Released`, `Picking`, `Picked`, `Packed`, `Shipped`, `Cancelled`), and the who/when of each
  transition. Owns its lines.
- `PickTask` — one line of demand: item/variant, `RequestedQuantity`, the `OutboundReference` (free
  text — an order number, a transfer request, whatever raised the demand), the `AllocatedBinId` and
  `AllocatedQuantity` the allocator assigned, `PickedQuantity` actually confirmed (may fall short —
  see business rule 11), `Status` (`Pending`, `Allocated`, `Picked`, `ShortPicked`, `Cancelled`).
- `PackTask` — one per wave. `PickWaveId`, `PackageCount`, `Note`, `PackedAt`.
- `ShipmentConfirmation` — one per wave, the point stock actually leaves the location.
  `PickWaveId`, `Carrier`, `TrackingNumber`, `ShippedAt`, and the ledger entries it posted (one per
  distinct SKU across the wave's picked lines).

*Cycle counts*

- `CycleCount` — a count of one or more bins, ad hoc or scheduled. `LocationId`, `ZoneId` (optional —
  narrows to one zone), `Status` (`Open`, `Finalized`), `ScheduledAt`, `FinalizedAt`. Owns its lines.
- `CycleCountLine` — one bin/SKU pair. `BinId`, item/variant, `SystemQuantity` (a `BinStock` snapshot
  taken when the line is opened, mirroring Stage 08's `StocktakeLine.SystemQuantity` exactly — not read
  live at finalize, for the same reason), `CountedQuantity`, and the `VarianceValue` posted once
  finalized.

**Application layer**

- `IZoneRepository`, `IBinRepository`, `IBinStockRepository`, `IBinStockMovementRepository`,
  `IPutawayTaskRepository`, `IPickWaveRepository`, `IPackTaskRepository`,
  `IShipmentConfirmationRepository`, `ICycleCountRepository`.
- `IBinStockMover` — the bin-level equivalent of Stage 08's `IStockLedgerPoster`: the one place a
  handler goes to move quantity between bins (posts `BinStockMovement`, maintains `BinStock`). Pure
  quantity bookkeeping, no valuation.
- `IPickAllocationStrategy` — given a pick line's demand, returns the bin(s) to allocate from. One
  implementation, `LargestBinFirstAllocationStrategy` (ADR-090).
- Extends `IStockLedgerPoster` (Stage 08, `VumaRetail.Application.Inventory`) with an optional
  `Guid? binId = null` parameter on `ReceiveAsync`/`AdjustAsync`/`PostStocktakeVarianceAsync`, and two
  new dedicated methods — `IssueForShipmentAsync` and `PostCycleCountVarianceAsync` — following the
  precedent `ReceiveForPurchaseAsync` set in Stage 12: a new caller of an existing movement type gets
  its own method and its own `StockReferenceType`, not a parameter on somebody else's. **ADR-087.**
- 19 commands, 11 queries, `WarehousePermissions` (9 permissions), `WarehouseModuleManifest` (flag
  `warehouse`, not core — a single-location shop with no bin discipline is a legitimate deployment).

**API** — `/api/v1/warehouse/...`, every operation in `/openapi/v1.json` with summaries, error
responses and permission requirements.

**Infrastructure** — eleven EF configurations, nine repositories, a reversible `Warehouse` migration,
DI registration.

**Seed** — `scripts/seed.sh` produces a warehouse that has really shelved and really shipped: a zone
and three bins, a putaway task confirmed into a bin, a pick wave picked, packed and shipped (stock
really left the location, checked against the ledger), and a cycle count that found and posted a
variance.

## Business rules

1. **A bin belongs to exactly one zone, and a zone to exactly one Stage 08 location.** Both references
   are plain `Guid` columns, never cross-schema foreign keys (`CONVENTIONS.md` §2); the application
   layer validates them against Stage 08's `IStockLocationRepository`.
2. **Putaway never changes a location's on-hand quantity, only where inside it stock sits.** It posts
   `BinStockMovement` only — it does not call `IStockLedgerPoster`, because Stage 12's goods receipt (or
   a Stage 08 manual receipt) already posted the location-level ledger entry that put the quantity on
   hand. A second call here would double it.
3. **A shipment is the only warehouse-owned event that changes what a location holds.** Confirming a
   shipment relieves the picked bins (already reserved at pick time) and posts one
   `IStockLedgerPoster.IssueForShipmentAsync` call per distinct SKU across the wave, valued at the
   location's current weighted-average cost — the same economic event as a Stage 08/09 sale issue, with
   its own reference type so it is never confused with one (ADR-085's precedent).
4. **A cycle count variance is real inventory variance, not a bin reshuffle**, and posts through
   `IStockLedgerPoster.PostCycleCountVarianceAsync` exactly as a Stage 08 stocktake's variance does —
   it changes the location's `StockBalance`, not only the bin's `BinStock`. A bin-level count that
   disagrees with the system is telling the shop it has more or less stock than it thought, not that
   the stock moved bins.
5. **Nothing is picked from a bin beyond what `BinStock` reports there.** The allocator only assigns
   bins carrying enough quantity; a picker confirming more than was allocated is refused.
6. **A pick may confirm less than allocated (a short pick).** The task's status becomes `ShortPicked`
   rather than being refused outright — a bin that turns out emptier than the system believed is
   exactly the kind of disagreement a cycle count exists to catch, not a reason to fail the whole wave.
   A short pick does **not** itself post a variance; that is what a cycle count is for.
7. **A wave ships as a whole or not at all per SKU line.** `ShipmentConfirmation` issues the sum of each
   SKU's `PickedQuantity` across the wave's lines — a partially picked wave can still ship what it
   actually picked, because "we shipped less than ordered" is a fulfilment fact the outbound
   reference's owner (a future Stage 14 sales order) needs to see, not a reason to block the delivery
   that is genuinely ready.
8. **A putaway task's confirmed bin need not be its suggested one.** The allocator's `SuggestedBinId` is
   advisory; the picker can scan a different bin (or split across two, by confirming a task partially
   and leaving the remainder `Pending` for a second putaway).
9. **Bin capacity is informational, not enforced** (deliberate, ADR-091) — this stage tracks a `Capacity`
   field for reporting and a future slotting stage, but does not refuse a putaway that would exceed it.
   A hard capacity refusal on day one, with no weighing/dimensioning data behind it, would block a
   legitimate putaway on a number nobody measured.
10. **A finalized cycle count is immutable**, the same shape Stage 08's `StocktakeSession.Finalize`
    already uses — no further counts, no second finalize, a correction is a new count.
11. **Everything is soft-deleted and tenant-scoped** (§7 rules 3 and 8), and no table in this schema
    draws a foreign key into another schema.
12. **No module names a GL account** (rule 12) — see "What this stage does not own".

## Tests / acceptance

**Unit** (Domain + Application, ≥ 80% line coverage on the stage's new code)

- `Bin`/`Zone` construction and code normalisation.
- `BinStock`: apply-in/apply-out arithmetic, refusing a negative result, refusing a unit-of-measure
  mismatch.
- `PutawayTask`: confirm into the suggested bin, confirm into a different bin, partial confirm leaving
  a remainder, refuse confirming a cancelled/already-confirmed task.
- `PickWave`/`PickTask`: allocation against `BinStock` (exact fit, largest-bin-first across two
  candidate bins, insufficient total stock refused), confirm a full pick, confirm a short pick and the
  resulting `ShortPicked` status, refuse confirming more than allocated.
- `ShipmentConfirmation`: aggregates picked quantity per SKU correctly across multiple pick lines for
  the same SKU in different bins; refuses shipping a wave with no picked lines.
- `CycleCount`: snapshot semantics (system quantity fixed at line-open time, not finalize time, mirroring
  Stage 08's `StocktakeLineTests`), finalize posts variance and closes, refuses a second finalize,
  refuses further counts once finalized.
- The extended `IStockLedgerPoster`: `binId` flows onto the posted `StockLedgerEntry` when supplied and
  is `null` when it is not (a Stage 08/09/10/12 call site that never passes it keeps behaving exactly as
  before — the regression this test exists to catch).

**Integration** (Testcontainers Postgres, real HTTP)

- The whole chain end to end: a location and a zone and three bins → a manual Stage 08 receipt (unbinned)
  → a putaway task confirmed into a bin → `BinStock` reflects it → a pick wave allocates from that bin
  → pick confirmed → pack → ship → the location's `StockBalance` really decreased by the shipped
  quantity and a `StockLedgerEntry` with the shipment's reference exists.
- A cycle count opened on that same bin after the shipment, counting a deliberately wrong quantity,
  finalized, and the location's `StockBalance` and the bin's `BinStock` both reflect the posted variance.
- The migration applies and reverses (`Down` tested).
- Every endpoint appears in `/openapi/v1.json`.
- Permission enforcement on putaway confirm, pick confirm, ship confirm and cycle count finalize.

**Architecture**

- `LayeringTests` sees the new code; no warehouse type names a GL account.
- `ReplicationRulesTests` sees every new replicated entity.
- `CommandClassificationTests` sees every new command (manifest-derived sweep, ADR-078).
- No cross-schema foreign key.

## Exit checklist

- [ ] `dotnet build -c Release` — zero warnings, zero errors
- [ ] `dotnet test` — all green, ≥ 80% line coverage on the stage's Domain + Application
- [ ] `Warehouse` migration generated, applied and reversed
- [ ] Every endpoint in `/openapi/v1.json` with summaries and error responses
- [ ] Permissions registered in the RBAC catalogue (`IModulePermissions`)
- [ ] Cycle count variances reach Stage 07 through Stage 08's existing seeded stocktake posting rules —
      no new posting rule needed, verified rather than assumed
- [ ] Module entitlement flag declared in the manifest
- [ ] Usage counters — automatic, via the `warehouse` schema (`UsageCounterSource` groups by schema)
- [ ] Replication scope declared on every new entity; `SYNC_AND_BACKUP.md` and `DATA_MODEL.md` updated
- [ ] `scripts/seed.sh` produces a warehouse that has really shelved and really shipped
- [ ] ADR-087 – ADR-091 appended to `docs/DECISIONS.md`
- [ ] The six agent reviews run (or documented why not), findings acted on or recorded
- [ ] `docs/PROGRESS.md` updated, committed

## Notes for later stages

- **Stage 14 (Order management)** becomes `PickWave`'s real demand source. `PickTask.OutboundReference`
  is free text today specifically so a real sales order id can fill it later without a shape change.
- **Stage 15 (Replenishment/MRP)** reads `BinStock` and `PutawayTask` history the way it will read
  `PurchaseRequisition` — this stage supplies the bin-level signal a slotting/replenishment engine
  needs, without building the engine.
- **Stage 24 (Logistics)** is where `ShipmentConfirmation.Carrier`/`TrackingNumber` graduate from
  free-text fields into a real carrier integration; nothing here assumes they stay text forever.
- **Bin capacity enforcement** (business rule 9) is a real gap for a dense warehouse, deliberately left
  for whichever stage adds bin dimensioning/weighing — enforcing a capacity nobody has measured would be
  worse than not enforcing one at all.
