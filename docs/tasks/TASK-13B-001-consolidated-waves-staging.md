# TASK-13B-001 — Consolidated waves and staging states

**Status:** COMPLETE (2026-09-11) · **Stage:** 13b · **Type:** Build (domain + application + infrastructure + tests)
**Depends on:** 13, 14, 06e, 08c — all present and verified.
**Reference reading:** `docs/stages/STAGE-13b-picking-waves-staging.md`, `docs/DECISIONS.md` ADR-113, ADR-114, ADR-115, ADR-087–ADR-091.

## Objective

Build geography-and-period pick waves that consolidate demand by SKU across orders, plus staging bins (Consolidation/Packing/Dispatch) that use the existing warehouse movement ledger.

## Scope

### Domain — `src/VumaRetail.Domain/Warehouse/`

- Extend `PickWave` with geography fields (`GeographyLevel`, `GeographyValue`, `PeriodFrom`, `PeriodTo`, `CompanyScope`), rebuild-from-snapshot capability.
- Extend `BinType` enum: add `Consolidation`, `Packing`, `Dispatch`.
- New entity `PickWaveLineBreakdown` — per-order split of a grouped wave line: `PickWaveLineId`, `OrderId`, `OrderLineId`, `Quantity`.
- New entity `CountSchedule` — cadence, scope, slow-mover selector, next run.
- New value objects: `GeoLocation` (Province/City/Suburb).
- `StagingState` derived from where quantity sits — `Shelved | Consolidation | Packing | Dispatch | Shipped`.
- Extend `BinStockMovementType`: add `ConsolidationIn`, `ConsolidationOut`, `PackingIn`, `PackingOut`, `DispatchIn`, `DispatchOut`.

### Application — `src/VumaRetail.Application/Warehouse/`

- `BuildConsolidatedWaveCommand` — finds qualifying open order lines, groups by (item, variant, uom, pack size), produces pick list + per-order breakdowns.
- `ReleaseConsolidatedWaveCommand` — allocates bins, produces pick tasks.
- `PreviewConsolidatedWaveQuery` — preview without committing.
- `CreateCountScheduleCommand` — schedule periodic counts.
- `GenerateCountSheetQuery` — produces CycleCounts from a schedule, warns on in-flight stock.
- Ports: `IOrderLineReader` (qualifying open order lines), `IPickWaveRepository`, `ICountScheduleRepository`.
- Permissions: `warehouse.wave.build`, `warehouse.wave.release`, `warehouse.count.schedule`, `warehouse.count.perform`.

### Infrastructure

- EF configs + repositories for new entities.
- Migration `Stage13b_PickingWavesStaging` (reversible, Down executed).
- `SYNC_AND_BACKUP.md` rows.
- `DATA_MODEL.md` §4n filled in.

### API — `/api/v1/pick-waves/consolidated` (build, preview, release),
`/api/v1/pick-waves/{id}/breakdown`, `/api/v1/count-schedules`, `/api/v1/counts/{id}/sheet`.

### Seed

Durban wave with grouped lines, picked into consolidation, interval count with in-flight warning.

## Out of Scope

- Handheld UI, Android app, new counting model (uses Stage 13's CycleCount).
- Cross-company wave coordination (that's 13b's later subtask — the common single-company wave first).

## Acceptance

- 10 Durban orders → one wave, two grouped lines (hot plates, gloves), breakdowns sum to 8 orders.
- Same filter at Province/Suburb picks up more/fewer.
- Address edit after build does not change the wave (geography snapshotted).
- Pick → consolidation → packing → dispatch → ship: available drops at reservation, on hand at ship.
- Count on item in dispatch bin warns and computes variance against on-hand including staging.
- Coverage ≥ 80% on Domain + Application.

## Tests Required

Unit: wave builder groups correctly, geography snapshot, breakdown sums, staging state transitions, count schedule generation, in-flight warning math.
Integration (real PG): full wave lifecycle, cross-company wave, interval count with in-flight warning.

## Verification update (2026-09-11)

The consolidated-wave and count-sheet handlers use the ambient tenant/store context, persist
per-order line breakdowns, read real Stage 14 sales-order demand through `SalesOrderLineReader`,
and calculate count-sheet quantities/warnings from actual bin stock. Warehouse unit verification is
green (75/75); the cross-company saga runs against two real PostgreSQL company databases, including
replay-safe creation and per-company compensation; and the Stage 13b migration has been verified
both up and down on scratch PostgreSQL. The full repository gates are green: unit 1,405/1,405,
architecture 83/83, integration 572/572. Specialist reviews were executed inline against their
briefs; no unresolved Stage 13b finding remains.
