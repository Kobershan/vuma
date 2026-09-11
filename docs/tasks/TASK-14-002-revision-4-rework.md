# Task

## Status

COMPLETE (2026-09-12)

## Stage

Stage 14 — Order Management

## Type

APPLICATION, INFRASTRUCTURE, DATABASE, TESTING, VERIFICATION

## Objective

Execute the stage doc's "Amendments — revision 4" rework: source allocation from Stage 08c's
reservation ledger (ADR-103), add COD settlement terms with a dispatch gate (ADR-111), and snapshot
delivery geography onto the order (ADR-113) — the three items the stage doc records as "not built
here", in dependency order (geography → COD → reservations).

## Why

Stage 14 allocates from a direct `PickTask` read (ADR-092, flagged "superseded in direction, not yet
in code"): two orders confirming concurrently can promise the same unit, because nothing holds it.
`AvailableToPromise` is computed as on-hand minus open PickTasks, blind to 08c holds, pro-forma
approvals and transfers. COD is a comments-field note with no gate. 13b's consolidated waves need a
geography snapshot that does not exist.

## Scope

- `DeliveryGeography` snapshot on `SalesOrder` (Province ← address Region, City, Suburb ← new
  optional command field, PostalCode), normalised at capture; null for click & collect. Migration.
  13b waves read the snapshot, never the live address.
- `SettlementTerms` (`Standard` default, `CashOnDelivery`) on the order; COD consumes no credit-group
  exposure; `ReleaseForDispatch` domain gate (Paid via `RecordOrderSettlementCommand`, or named
  driver-collect authorisation); enforced in `ShipWaveCommandHandler` through a new
  `IOrderDispatchGate` port implemented in Orders (no new module edge direction).
- Confirm + Reattempt reserve through 08c `IReservationService` (`ReservationSource.Order`,
  `groupDocumentRef` = order number); `ReservationId` on the line (reference, not a figure — ADR-092
  no-copy rule holds); cancel releases; refresh consumes newly-fulfilled lines.
  `OrderFulfilmentReader.GetAvailableToPromiseAsync` delegates to 08c `IAvailabilityService`
  (authoritative Available, never negative).
- Invoices inherit COD terms where `GenerateInvoicesFromOrderCommand` produces them (ADR-111:
  printed on invoice).
- Tests: unit (snapshot, COD gate incl. driver-collect default-config note, reserve/release/consume,
  reader delegation, the §carry-forward unbinned-stock backorder test) + integration on real PG
  (concurrent-confirm never-negative proof, COD ship refusal then release, wave-groups-by-snapshot).
- Migration reversible (`Down` executed). OpenAPI `Produces` annotations on touched endpoints.

## Out of Scope

- 13b's wave-build handler (separate task; consumes the snapshot built here).
- Per-tenant geography reference list (ADR-113's phrase) — snapshot stores normalised strings;
  reference data is a later stage. Reason in PROGRESS.md, not silence.
- `RecordOrderSettlementCommand` end-to-end callers (needs 09/10b integrators — still reserved).

## Architecture

ADR-099/103 (one DB, saga legs; Available answers), ADR-108 shape for hold-on-commit,
ADR-111/113 as specified. No handler touches two databases. Modules raise events; rules decide
accounts. Approval engine untouched.

## Dependencies

Stages 08c (reservation/availability services), 10c (invoice terms inheritance), 13 (wave ship hook).

## Relevant Files

- `src/VumaRetail.Domain/Orders/SalesOrder.cs`, `SalesOrderLine.cs`, `OrderEnums.cs`
- `src/VumaRetail.Application/Orders/Commands/OrderCommands.cs`, `OrderFulfilmentReader.cs`
- `src/VumaRetail.Application/Warehouse/Commands/PickPackShipCommands.cs` (ship hook)
- `src/VumaRetail.Application/Inventory/AvailabilityPorts.cs` (08c ports)

## Relevant Documentation

`docs/stages/STAGE-14-order-management.md` (Amendments), `docs/MULTI_COMPANY.md` §4–§6,
ADRs 099/102/103/108/111/112/113/116, `CLAUDE.md` §7 rules 6/7/12/20.

## Data/Database Impact

Migration: `sales_orders` += geography columns + `settlement_terms` + driver-collect columns;
`sales_order_lines` += `reservation_id` (nullable). Reversible, `Down` executed.

## Acceptance Criteria

1. Two orders confirming concurrently for the last unit: one holds, one backorders; Available never negative (integration).
2. COD order refuses ship; records payment → ships; or named driver-collect → ships (integration).
3. Wave groups by snapshot after address edit (integration; 13b query reads snapshot).
4. Cancel releases holds (ledger gains release rows; availability restored).
5. Coverage ≥80% on new Domain + Application; arch green; unit full-suite green.

## Definition of Done

- [ ] Criteria 1–5 green on real PostgreSQL
- [ ] Migration reversible, `Down` executed
- [ ] `money-and-tax`, `multi-company-guard`, `stock-availability-guard` findings closed or reasoned
- [ ] `docs/PROGRESS.md` updated, ADRs appended, committed and pushed
