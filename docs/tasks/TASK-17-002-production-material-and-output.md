# TASK-17-002 — Production material, output and scrap accounting

**Status:** IN_PROGRESS · **Stage:** 17 · **Type:** Domain, application, infrastructure, integration

## Objective

Implement reservation, atomic material issue, finished-output receipt and scrap recording through
the owning company’s stock ledger and existing financial event/posting boundaries.

## Dependencies

TASK-17-001 and Stage 08/08c stock reservation/ledger ports.

## Acceptance criteria

- A shortage refuses material atomically without negative stock.
- Duplicate issues, receipts and scrap operations do not duplicate stock or WIP postings.
- `Ten_units_consume_twenty_components_once` and `Scrap_reconciles_wip` pass against PostgreSQL.
- Every operation is auditable, tenant/company scoped and queued through the shared outbox.

## Work log

- 2026-09-12: canonicalized from Stage 17 part 17-P02. Implementation not yet started.
- 2026-09-13: added idempotent aggregate records for material issues, finished output and scrap,
  including changed-payload rejection, requirement/over-issue protection and exact output-plus-scrap
  reconciliation before completion. Focused manufacturing tests pass 18/18 locally. Stock ledger,
  reservation, financial event, persistence and PostgreSQL scenario evidence remain open.
- 2026-09-13: the first pushed checkpoint exposed that the generated request-identity migration was
  incorrectly a duplicate table-create migration. It was removed and regenerated as additive
  migration `20260913053229_Stage17_ProductionExecutionRecords`, adding the BOM identity and three
  JSON execution-record columns to the existing production-order table. The failed workflow was
  `34740105395`; no replacement is pushed until migration validation is green.
- 2026-09-13: added production-specific stock-ledger movement/reference classification and
  `IStockLedgerPoster` methods for component issue and finished-output receipt. Command handlers now
  validate the aggregate operation first and skip the poster on exact replay, preventing duplicate
  stock and valuation events; changed payloads remain conflicts. Infrastructure and focused unit
  builds pass locally. Scrap accounting and PostgreSQL stock/financial scenarios remain open.
- 2026-09-13: added the production reservation boundary. A new `ReservationSource.Production` hold
  is taken at the requested stock location, shortfall releases the partial hold and fails without
  issuing stock, successful issues consume the hold, and poster failures release it. This is covered
  by the application build; the real-PostgreSQL shortage/issue/replay and financial-event scenarios
  remain required before task closure.
- 2026-09-13: CI run `34743485288` passed Build, Test, Architecture, Vulnerability, Migration,
  Design System and Windows/Android packaging gates for the location and lifecycle boundary.
- 2026-09-13: added `IProductionAccountingEventPublisher`; each newly accepted scrap operation now
  raises `manufacturing.scrap.recorded` through Stage 07's posting boundary, while exact replays are
  silent and finance-less hosts retain a logging fallback. Infrastructure builds and the focused
  manufacturing suite pass 19/19; PostgreSQL shortage/replay and journal assertions remain open.
- 2026-09-13: CI run `34746409342` passed Build, Test, Architecture, Vulnerability, Migration,
  Design System and Windows/Android packaging after the accounting boundary was wired.
