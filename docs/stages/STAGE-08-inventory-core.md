# STAGE 08 — Inventory Core: Stock Ledger, Valuation, Transfers, Stocktakes

**Status:** DONE (2026-08-15) · **Depends on:** 07 · **Reference reading:** `docs/DATA_MODEL.md` §1–§3
(mandatory columns, types, schema conventions), `docs/CONVENTIONS.md` §1–§5, `docs/DECISIONS.md`
ADR-005 (the append-only ledger this stage implements), ADR-007 (conflict policies), ADR-016 and
`CLAUDE.md` §7 rule 12 (no module names a GL account).

## Task index

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-08-001 | Complete inventory core | Stage 07 | COMPLETE |

## Objective
What is on the shelf, what it cost, and every event that changed either. Stage 06 said what a tenant
sells; this stage says how much of it there is and what it is worth, as an append-only ledger with a
maintained projection rather than a mutable quantity column.

This stage owns **quantity and cost**, not the things that move them:
- No sale, till or tender — that is Stage 09. `RecordSaleIssueCommand` exists and works, and is the
  command POS will call; nothing here originates a sale.
- No purchase order, GRN or three-way match — that is Stage 12. A receipt here is a bare "stock
  arrived at this cost", with no supplier document behind it.
- No bins, zones, putaway or picking — that is Stage 13. A location is the smallest unit stock is
  tracked at; Stage 13 subdivides it.
- No GL account anywhere. A movement raises an `IFinancialEvent` naming an event type and one amount;
  Stage 07's posting rules engine decides which accounts it lands on (rule 12).

## Deliverables

**`inventory` module** (schema `inventory`, six tables)
- `StockLocation` — a physical place stock is held. Unique `Code` per tenant (upper-cased, like
  `Store.Code`), `Name`, `StockLocationType` (`Warehouse`, `SalesFloor`, `Other`), `IsActive`.
  `StoreId` is optional: a central distribution warehouse serving several stores is a legitimate
  tenant-wide location (R8). Deactivate, not delete.
- `StockLedgerEntry` — the append-only record of one movement. `IImmutableRecord`, so the persistence
  layer refuses it in the `Modified` or `Deleted` state (§7 rule 7) and the guarantee is structural
  rather than conventional. Carries a **signed** `Quantity` — positive in, negative out — so summing
  the table gives on-hand without a case statement. `StockMovementType` covers receipt, sale issue,
  adjustment, both transfer sides and stocktake variance; `StockReferenceType` + `ReferenceId`
  correlate it to its document. An adjustment must carry an `AdjustmentReasonCode`.
- `StockBalance` — the projection the ledger sums to: `QuantityOnHand` and weighted-average
  `AverageCost` per location per stock-keeping unit. Maintained transactionally alongside every
  ledger insert, never written independently of one. Node-local, not replicated (ADR-069).
- `StockTransfer` — the document correlating the two ledger entries a transfer always posts. Its id is
  minted *before* either entry, so both can carry it as their `ReferenceId`.
- `StocktakeSession` / `StocktakeLine` — a physical count at one location. A line snapshots the system
  quantity at the moment of counting, not at finalize, so the variance is not blamed for sales made
  while the count was running. Finalizing posts every line's variance and closes the session
  permanently.

Every item/variant reference is a plain `Guid` column with an index, never a foreign key into
`catalog` (CONVENTIONS.md §2). `StockKeepingUnitResolver` validates them through catalog's published
repository ports — the one place inventory reaches into another module, and it goes through a contract.

**Application** — `StockLedgerPoster` is the single service all six posting paths go through, so the
weighted-average arithmetic and the ledger/balance/event sequencing exist in exactly one place.
Commands for location create/deactivate, receive, sale issue, adjust, transfer, and the three
stocktake operations. Queries for locations, balances, a keyset-paginated ledger and transfers.

**Infrastructure** — EF configurations for all six tables, the `Inventory` migration, five
repositories, and `AddVumaInventory`.

**Web** — 15 endpoints under `/inventory`. There is no `PUT` or `DELETE` on a ledger entry and there
never will be: a correction is a new compensating movement.

## Business rules
1. **Stock levels are never a mutable column.** Every change is a new ledger row; the balance is a
   projection maintained in the same transaction (ADR-005, §7 rule 6).
2. **Nothing already written is updated or deleted.** A mistake is corrected by a compensating entry,
   the way a financial document is amended by a credit note.
3. **Valuation is weighted-average moving cost** (ADR-068). A receipt blends into the average; an
   issue relieves at it without changing it.
4. **Stock cannot go negative through any path.** An issue, transfer-out or negative adjustment beyond
   what is on hand is refused. A physical count that disagrees is a stocktake variance, not a licence
   to go below zero.
5. **A transfer moves value, it does not create it.** The destination receives at exactly the source's
   average cost.
6. **Exactly one of an item or a variant** identifies every movement — enforced in the domain by
   `StockItemReference` and again by a check constraint on each table.
7. **A movement's quantity must be in the stock-keeping unit's own unit of measure.** Converting is the
   caller's job, through the Stage 06 unit-of-measure catalogue.
8. **An adjustment carries a documented reason code.** "Documented" means somebody had to say why.
9. **A finalized stocktake is immutable.** No further counts, no second finalize.
10. **No module names a GL account** (rule 12). A movement raises an event type and one amount.

## Tests / acceptance
- Unit: weighted-average arithmetic across receipt/issue/receipt sequences, including an average that
  does not divide evenly and an emptied balance that keeps its cost basis; ledger entry invariants;
  transfer and stocktake rules; location code normalisation.
- Integration, against real PostgreSQL through the real dispatcher: the balance equals the sum of the
  ledger after five mixed movements; a refused issue writes nothing; a transfer posts two correlated
  entries and conserves total value; a refused transfer credits nothing at the destination; finalizing
  posts variances and closes; a recount replaces rather than adds; keyset paging returns every row
  once in descending order; every movement raises exactly one valuation event carrying its value.
- The seed produces a demonstrable store: two locations, six posting rules, stock on hand.

## Exit checklist
- [x] Six tables built with the append-only, uniqueness and exactly-one-SKU rules enforced in the
      domain and again as database constraints
- [x] Migration applied and reversed against a real Postgres instance
- [x] Keyset pagination implemented and tested for `ListStockLedgerEntriesQuery`
- [x] RBAC permissions registered; `InventoryModuleManifest` registered as core
- [x] Valuation events wired to Stage 07's posting rules engine, with the no-rule path defined
      (ADR-070) and the logging fallback kept for hosts without finance
- [x] `docs/DATA_MODEL.md` §4e written; ADR-068 to ADR-071 appended
- [x] Seed data added — locations, accounts, posting rules, opening receipt
- [x] Full suite green: unit 428/428, architecture 31/31, integration 285/285
- [x] End-to-end `--seed` run against a real database, proving the module resolves in the container
- [x] `docs/PROGRESS.md` updated, committed as `feat(stage-08): inventory core — ledger, valuation,
      transfers, stocktakes`

## Notes for later stages
- **Stage 09 (POS)** calls `RecordSaleIssueCommand` with the sale's id as `SaleReferenceId`. The
  command, its permission and its GL path already work; POS supplies the sale.
- **Stage 12 (Procurement)** will want a receipt that references a GRN rather than a manual one.
  `StockReferenceType` takes a new member and `IStockLedgerPoster.ReceiveAsync` a reference argument;
  nothing about the ledger shape changes.
- **Stage 13 (Warehouse)** subdivides a location into bins. The ledger's `LocationId` stays; a
  `BinId` beside it is additive.
- **Cross-server transfers.** This stage builds the synchronous, single-node case — both locations
  served by one store server, both entries in one transaction. An asynchronous transfer with an
  in-transit state and a receiving confirmation fits the same document shape without changing it.
- **Costing methods.** ADR-068 chose weighted average. FIFO or lot costing needs real cost layers, an
  additive change beside the balance rather than a rewrite, because every posting already goes
  through `StockLedgerPoster`.
