# STAGE 13b — Consolidated picking waves, staging states and interval counts

**Status:** NOT_STARTED · **Depends on:** 13, 14 · **Reference reading:** `docs/stages/STAGE-13-*.md` (pick waves, bins, cycle counts), `docs/DECISIONS.md` ADR-113, ADR-114, ADR-115, ADR-087–ADR-091

## Objective

> "The picking list must have items for a period per town or city, so everything for that town can be
> pulled out one time. Ten customers from Durban: five need two hot plates each, three need five gloves
> each — group all of it. And it must be pickable by province or suburb too."

Three things, all extensions of Stage 13 rather than new machinery:

1. **Geography-and-period pick waves** that consolidate demand by SKU across many orders.
2. **Staging states** — where stock actually is once it leaves the shelf: consolidation, packing,
   dispatch.
3. **Interval counts** — scheduled counts that target what nobody has touched, and that tell the
   checker when the stock they are counting is in flight.

## Deliverables

**Geography**
- `GeoLocation` value object on the delivery address: `Province`, `City`, `Suburb`, `PostalCode`,
  normalised on capture from a per-tenant town/suburb reference list, and **snapshotted onto the
  order** so a later address edit cannot silently move a wave's contents (ADR-113).
- Reference data import for the geography list reuses Stage 11's pipeline — no new importer.

**Wave building**
- `PickWaveFilter` — period (from/to), geography level (`Province | City | Suburb`) and value, optional
  company scope, optional delivery run, optional order status.
- `BuildConsolidatedWaveCommand` — finds every qualifying open order line, **groups by
  (item, variant, uom, pack size)** across orders, and produces:
  - a **pick list by SKU** — one line per SKU with the total quantity, and the bins to draw it from via
    Stage 13's existing `IPickAllocationStrategy`,
  - the **breakdown per order/customer** carried alongside each grouped line, so consolidation can
    split it back without a second query.
- **A wave may span companies, and companies are separate databases** (ADR-099). A cross-company wave is
  coordinated in the registry — the wave header and its grouped lines — while every `PickTask`,
  `BinStockMovement` and `ShipmentConfirmation` stays inside its own company's database. Building one is
  a saga (ADR-116): one leg per company, compensated by cancelling that company's tasks if a later leg
  fails. The pick consolidates; the documents, the stock and the books do not.
- A single-company wave — which is most of them — touches the registry not at all and behaves exactly as
  Stage 13's does today. Do not make the common case pay for the cross-company one.

**Staging states**
- Staging areas are **bins of a special type** — `Consolidation`, `Packing`, `Dispatch` — so movement
  uses Stage 13's existing `bin_stock_movements` ledger and nothing parallel is invented (ADR-114).
- Picking confirms stock from a shelf bin into the wave's consolidation bin; packing moves it to the
  packing bin; dispatch moves it to the dispatch bin; shipment confirmation takes it out the door and
  writes the stock ledger, exactly as Stage 13 already does.
- `StockLocationState` is therefore derived — `Shelved | Consolidation | Packing | Dispatch | Shipped`
  — from where the quantity sits, not a flag anybody has to remember to set.
- Stock in a staging bin is **not available** (Stage 08c's `InStaging`), and is still on hand.

**Interval counts**
- `CountSchedule` — cadence (daily/weekly/monthly/custom), scope (zone, class, value band, supplier),
  and a **slow-mover selector**: items with no movement in N days, weighted so the least-touched are
  counted first, plus a random sample so the schedule cannot be gamed.
- Generates Stage 13 `CycleCount`s. No new counting model.
- **In-flight warning**: when a count line's SKU has quantity in a consolidation, packing or dispatch
  bin, the count sheet says so, with the quantity and the wave/shipment reference, and the variance
  calculation accounts for it. The checker is told, not blocked (ADR-115).

**API** — `/api/v1/pick-waves/consolidated` (build, preview, release),
`/api/v1/pick-waves/{id}/breakdown`, `/api/v1/count-schedules`, `/api/v1/counts/{id}/sheet`.

**Permissions** — `warehouse.wave.build/release`, `warehouse.count.schedule`, `warehouse.count.perform`.

## Business rules

1. A wave's geography and period are recorded on the wave; rebuilding it later with the same filter is
   reproducible from the snapshot, not from live addresses.
2. Grouping is by (item, variant, uom, **pack size**) — two orders for the same item in different pack
   sizes are two grouped lines, because that is two different things to pick.
3. Every grouped line carries its per-order breakdown. Consolidation cannot guess.
4. An order line may be in exactly one open wave, and that uniqueness is enforced in the line's own
   company database — the registry coordinates, it does not own the constraint.
5. Stock in staging is on hand and not available. Never the other way round.
6. A count sheet shows in-flight quantity per line where it exists, and the variance is computed
   against on-hand including staging, not against shelf quantity.
7. A count schedule never selects an item that is currently being picked in an open wave for a shelf
   bin that wave is drawing from — it defers that line to the next run and says why.

## Tests / acceptance

- The operator's example: 10 Durban orders, 5 × 2 hot plates and 3 × 5 gloves, built for a week's
  period at `City = Durban` → one wave, two grouped lines (10 hot plates, 15 gloves), breakdowns
  summing back to the 8 orders.
- The same filter at `Province = KwaZulu-Natal` picks up the Durban orders plus Pietermaritzburg's; at
  `Suburb = Umhlanga` it picks up only that suburb's.
- An address edited after the wave is built does not change the wave.
- A cross-company Durban wave over two databases: two sets of pick tasks, one consolidated pick list, and
  a failed second leg leaves the first company's tasks cancelled and no wave.
- Pick → consolidation → packing → dispatch → ship: available drops at reservation, on hand drops at
  ship, and the bin movement ledger tells the whole story with no gaps.
- A cycle count on an item with 6 units in the dispatch bin warns the checker and computes variance
  against 6 + shelf, not shelf alone.
- A slow-mover schedule over a seeded warehouse selects the untouched items first and includes the
  random sample.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `stock-availability-guard`, `architecture-guard`, `sync-and-offline` run, findings closed
- [ ] Migration reversible, `Down` executed
- [ ] Handheld flows updated so a picker's confirm writes the staging move
- [ ] `docs/DATA_MODEL.md` §4k extended; replication registry updated
- [ ] Seed: a Durban wave, picked into consolidation, one interval count with an in-flight warning
