# Task

## Status

COMPLETE

## Stage

Stage 08c — Cross-company availability, reservations & split fulfilment

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE, API, TESTING

## Objective

Build the company-local half of Stage 08c: the append-only reservation ledger
(`inventory.stock_reservations`), the `inventory.available_balances` projection with its
rebuild-equals-incremental guarantee, the `AvailableToPromise` value object, and the two
services that make "available never goes negative" structural rather than conventional —
`IAvailabilityService` (local authoritative reads vs group planning reads as *different*
types) and `IReservationService` (reserve/consume/release/expire inside one company's own
database, one serialisable transaction with the availability re-check and a row lock).

## Why

Every later question in this stage (which company fills this line, split fulfilment, expiry)
reduces to one primitive: take a hold on a stated quantity of a stated stock-keeping unit in
a stated company without ever driving available negative under any interleaving. Stage 08 has
on-hand only; Stage 13's `BinStock.QuantityReserved` is bin-scoped for pick waves, not a
document-grade hold. Without this task's ledger there is nothing for TASK-08C-002's saga legs
to write.

## Scope

- Domain (`src/VumaRetail.Domain/Inventory/`):
  - `StockReservation` — `Entity` + `IImmutableRecord` +
    `[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]` (same shape as
    `StockLedgerEntry`; immutable rows accumulate, two nodes never overwrite). Columns:
    company (base `CompanyId`), location, item/variant (exactly-one, `StockItemReference`),
    positive `Quantity`, source document (`ReservationSource`: `Order`, `ProFormaApproval`,
    `Transfer`, `Shipment`), source document id, `GroupDocumentRef` (nullable, ties the two
    legs of a cross-company order together), `ReservationId` (logical id shared by a hold and
    its terminal row), `SequenceNumber`, `State` (`Held`, `Consumed`, `Released`, `Expired`),
    `ExpiresAt` (nullable), `Reason`, nullable `IntentId`/`LegId` (saga idempotency key for
    TASK-08C-002), nullable `ConsumedByReferenceId`.
  - Factories: `Hold(...)`, `Consume(held, consumedByReferenceId, at)`, `Release(held, reason,
    at)`, `Expire(held, at)` — each returns a *new* row; the held row is never mutated. Domain
    refuses: non-positive hold quantity, terminal row against a non-held state, UoM drift
    within one `ReservationId` chain.
  - `AvailableToPromise` value object — `OnHand`, `Reserved`, `InStaging`, `Incoming`,
    `Available` (= OnHand − Reserved − InStaging), `AsAt`. `Incoming` is carried, not added:
    only physically present, unreserved, unstaged stock answers "can I sell this" (ADR-103).
  - `AvailableBalance` entity — the projection: location, item/variant, on-hand mirror,
    reserved, in-staging, incoming, `AsAt`. Mutable projection (like `StockBalance`, NOT
    `IImmutableRecord`), `NodeLocal` replication (a running total is not mergeable — same
    reasoning as `StockBalance`'s remarks).
  - Extend `InventoryRuleException` with reservation refusals (insufficient available,
    reservation not held, expiry in the past for a hold that carries one).
- Application (`src/VumaRetail.Application/Inventory/`):
  - Ports in `InventoryPorts.cs`: `IStockReservationRepository` (add, find open by
    `ReservationId`, list open by location+SKU, list due-for-expiry), `IAvailableBalanceRepository`
    (find/add/update, list for location, list all for rebuild).
  - `IAvailabilityService`: `GetLocalAsync` → `LocalAvailability` (authoritative, no `AsAt`
    hedging — it was read inside the owning company) and `GetGroupAsync` →
    `GroupAvailabilityView` (planning only, per-contributor `AsAt` + stale flags, built from
    the registry projection in one read). Different return types by construction.
  - `IReservationService`: `ReserveAsync`, `ConsumeAsync`, `ReleaseAsync`, `ExpireDueAsync`
    (single-company scope helper for the TASK-08C-002 expiry job). Every method runs inside
    ONE company database: serialisable transaction, `SELECT … FOR UPDATE` on the
    `available_balances` row (insert-then-re-read on first hold, unique index as backstop),
    re-check available, insert ledger row(s), update projection, publish
    `inventory.availability.changed` — all in the same transaction (ADR-006 shape).
  - Commands (`ReserveStockCommand`, `ConsumeReservationCommand`, `ReleaseReservationCommand`)
    with `[CommandSideEffect(SideEffect.Write)]` + FluentValidation validators; queries
    (`GetLocalAvailabilityQuery`, `GetGroupAvailabilityQuery`) side-effect-free. Handlers take
    repositories/services only — never `VumaRetailDbContext`, never `IUnitOfWork`
    (MultiCompanyGuardTests + PipelineRulesTests enforce).
  - Permissions: `inventory.availability.view`, `inventory.reservation.manage` on
    `InventoryPermissions`. Group-scoped read is gated on `registry.availability.view` (new,
    on `RegistryPermissions`): the stage text says `group.view`, but ADR-013 (LOCKED) requires
    `module.entity.action` and ADR-139 already corrected two-segment names once — `group.view`
    would fail the permission-shape test. This is recorded here, not guessed elsewhere.
- Infrastructure:
  - `inventory.stock_reservations` + `inventory.available_balances` configurations
    (exactly-one-SKU check constraints; partial unique `ux_stock_reservations_open` on
    `(reservation_id) WHERE state = 'Held'`; partial unique on `(intent_id, leg_id) WHERE
    intent_id IS NOT NULL` for saga idempotency; unique balance per location+SKU mirroring
    `stock_balances`' split-index pattern), repositories, services, migration (reversible).
  - `registry.group_availability_rows` configuration + `GroupAvailabilityProjectionApplier`
    (applies one `inventory.availability.changed` event to the registry projection,
    idempotent on event id) + `RegistryGroupAvailabilityReader` (ONE read, stale threshold 15
    minutes default, stale contributors named not summed-silently — ADR-119).
  - DI wiring in the inventory/registry service-collection extensions.
- API (`src/VumaRetail.Web/Inventory/` + `src/VumaRetail.Contracts/Inventory/`):
  - `GET /api/v1/availability?groupScope=true` (group view, `registry.availability.view`;
    without the flag, company-local, `inventory.availability.view`). Every figure carries
    `AsAt` (business rule 6).
  - Reservation endpoints for internal callers (reserve/consume/release under
    `/api/v1/inventory/reservations`, `inventory.reservation.manage`).
  - DTOs + OpenAPI examples + error responses; no domain entities cross the boundary.
- Seed: extend `DemoSeed` with one reserved order line visible in availability (exit-checklist
  seed starts here; the two-company order completes in TASK-08C-002).

## Out of Scope

- Sourcing strategy, saga commit, split documents, expiry job/hosting (TASK-08C-002).
- Verification sweep, coverage measurement, DATA_MODEL §4m prose (TASK-08C-003 owns the doc
  update; this task keeps code + migration + tests green).
- Reworking Stage 14 allocation onto this ledger (PROGRESS.md records it as future rework).
- Touching `SagaCoordinator.DispatchLegAsync` (documented no-op; TASK-08C-002 consumes saga
  *records* directly and records why — 07C-004 owns the shared dispatch table).

## Architecture

- ADR-005 shape: reservations are append-only; a release/consume/expire is a new row, never an
  edit (`IImmutableRecord` makes it structural). ADR-103: reservation reduces available,
  leaves on-hand alone; on-hand still changes only through `IStockLedgerPoster`.
- ADR-116: no handler touches two databases. `IReservationService` operates on the ambient
  company context only; the `MultiCompanyGuardTests` static scan must stay green (one
  `.CreateAsync(` call site per file at most — the service takes repositories, not factories).
- ADR-119: the group view is stale by design, carries `AsAt`, never decides a commit. The two
  return types (`LocalAvailability` vs `GroupAvailabilityView`) make substitution a compile
  error, per the stage deliverable.
- §7 rule 12: no GL account names anywhere near this (reservations post no journals; the
  valuation event publisher is untouched).
- `InStaging` source: Stage 13 ADR-114 derives staging from bins of type
  Consolidation/Packing/Dispatch. This task reads it through a tiny
  `IStagingQuantityReader` port with a zero-default implementation if warehouse exposes no
  stable query seam — overstating available is the defect; a seam defaulting to zero with the
  integration point named is the honest cut. (Confirmed during implementation; recorded in Work Log.)

## Architectural Boundaries

- Domain references nothing (LayeringTests). Application references abstractions only.
- No cross-schema FK (CONVENTIONS.md §2): item/variant/location/company are bare ids.
- Every command carries `[CommandSideEffect]` (CommandClassificationTests); queries carry none.
- New entities carry `[Replicated]` (PersistenceRulesTests) + private EF ctor
  (ReplicationRulesTests).

## Dependencies

Stages 06c, 06d, 06e, 08 (all on `main`). No dependency on 07C-004.

## Relevant Files

- `src/VumaRetail.Domain/Inventory/StockBalance.cs` (projection precedent),
  `StockLedgerEntry.cs` (immutable-ledger precedent), `InventoryExceptions.cs`
- `src/VumaRetail.Application/Inventory/InventoryPorts.cs`, `StockLedgerPoster.cs`,
  `Permissions/InventoryPermissions.cs`, `Queries/InventoryQueries.cs`
- `src/VumaRetail.Infrastructure/Persistence/Configurations/Inventory/InventoryConfigurations.cs`,
  `Persistence/Repositories/` (inventory repos), `Persistence/VumaRetailDbContext.cs`,
  `Persistence/VumaRegistryDbContext.cs`, `Registry/GroupReadStore.cs`
- `src/VumaRetail.Web/Inventory/InventoryEndpoints.cs`,
  `src/VumaRetail.Contracts/Inventory/InventoryContracts.cs`
- `tests/VumaRetail.IntegrationTests/Inventory/InventoryHarness.cs`,
  `tests/VumaRetail.UnitTests/Inventory/`

## Relevant Documentation

`docs/stages/STAGE-08c-availability-sourcing.md`, `docs/MULTI_COMPANY.md` §4–§5,
ADR-005/102/103/116/119, `CLAUDE.md` §7 rules 3/4/5/6/8/9, `docs/DATA_MODEL.md` §1–§2b/§4e,
`docs/TESTING.md`, `docs/CONVENTIONS.md` §2/§4/§5.

## Implementation Requirements

- Row-lock pattern for the reserve path: load the `available_balances` row
  `FOR UPDATE` inside a serialisable transaction; on missing row, insert then re-read (unique
  index backstop, retry once on 23505). The re-check (`available >= demanded`) happens after
  the lock, in the same transaction — check-then-act across statements without the lock is the
  defect this stage exists to prevent.
- Partial allocation at the primitive level: `ReserveAsync` reserves `min(available, demanded)`
  and reports the shortfall — it never throws on shortfall (throwing is the caller's policy,
  TASK-08C-002's re-source/backorder decision, business rule 3). It throws only on negative
  arithmetic or missing location/SKU.
- `ConsumeAsync` requires the held quantity to still be held; consuming more than held is a
  refusal, not a clamp (a clamp would silently un-reserve someone else's hold).
- Outbox: reservation rows are `[Replicated]` so the pipeline's outbox behaviour captures
  them; the `inventory.availability.changed` projection event is published in the same commit.
  Resolve during implementation whether the generic behaviour's payload suffices for the
  applier or a dedicated event row is needed — keep whichever is smaller, record the choice.
- Quantities `decimal(18,6)` + UoM, money untouched (reservations carry no money — cost lives
  on the ledger; a reservation valued at cost would recognise value that has not moved).

## Data/Database Impact

- New migration: `inventory.stock_reservations`, `inventory.available_balances`,
  `registry.group_availability_rows`. Reversible (`Down` drops in reverse order). No change to
  existing tables.
- Replication registry (`docs/SYNC_AND_BACKUP.md` §3 entity table): `StockReservation` =
  StoreToCloud/AppendOnly; `AvailableBalance` = NodeLocal (projection, rebuilt per node like
  `StockBalance`).

## API Impact

- `GET /api/v1/availability` (+`groupScope`), `POST /api/v1/inventory/reservations/*` — all in
  `/openapi/v1.json` with examples and error responses (§8 DoD).

## Security

- Endpoints gated on the new permission constants (no hardcoded strings).
- Reservation reasons/notes are operator text: length-capped, trimmed, never rendered as HTML
  anywhere new.

## Multi-Company/Tenant Impact

- Everything in this task is company-local: `CompanyId` stamped by `ApplyCompanyIdentity`
  from the ambient `ICompanyContext`; the tenant filter applies as usual. Cross-company work
  is TASK-08C-002.
- Registry projection rows are keyed `(tenant_id, company_id, location_id, item, variant)`.

## Sync/Offline Impact

- Reservation rows replicate StoreToCloud/AppendOnly (auditable hold history survives to the
  cloud). `AvailableBalance` is NodeLocal. The registry projection is fed by company outbox
  events; `RebuildAsync` (registry asks each company to republish / re-reads balances +
  reservations) must equal the incremental projection — TASK-08C-001's integration test proves
  it after 500 random movements/reservations.

## Acceptance Criteria

1. 20 demanded with local available 12 → 12 held, 8 shortfall reported, available left 0.
2. Reserve → release → reserve leaves availability exactly where it started, with three ledger
   rows (Held, Released, Held).
3. Two concurrent reserves for the last 5 units in one company: one holds 5, the other holds 0
   with shortfall 5. Real PostgreSQL, genuinely parallel transactions (two connections,
   `Task.WhenAll`, barrier start).
4. Availability projection rebuilt from scratch equals the incremental projection after 500
   random movements and reservations (same-SKU parallel pairs included).
5. `GET /api/v1/availability` (local) and `?groupScope=true` (group) both carry `AsAt`; group
   names stale contributors past the threshold instead of silently summing.
6. Coverage ≥ 80% on this task's Domain + Application (measured in TASK-08C-003, written here).

## Tests Required

- Unit: hold/consume/release/expire state machine + refusals; ATP arithmetic (never negative
  by construction); `AvailableBalance` application; idempotency-key uniqueness shaping.
- Integration (real PG): criteria 2–4; serialisable-leg isolation check (no 40001 escapes as a
  500 — retried or answered as a refusal per `docs/API_STANDARDS.md`); migration `Up`/`Down`
  round-trip on a scratch database.
- Architecture: existing suites stay green (no new violations introduced).

## Edge Cases

- First-ever hold for a SKU (no balance row, maybe no stock row): creates projection row,
  available 0 → shortfall = demanded, no throw.
- UoM mismatch between demanded and held/reserved chain → refusal.
- Expire of an already-consumed hold → refusal (terminal rows are final).
- Two different locations, same SKU: independent rows, independent locks (no cross-talk).
- Tenant filter: reservations for tenant B invisible from tenant A (harness test).

## Definition of Done

- [ ] `dotnet build -c Release` zero warnings in Domain/Application, zero errors anywhere
- [ ] `dotnet test` green; new code ≥ 80% line coverage on Domain + Application
- [ ] Migration generated, applied, reversible (`Down` tested on scratch DB)
- [ ] Endpoints in `/openapi/v1.json` with examples + error responses
- [ ] Permissions registered in catalogue; module manifest unchanged (no new module)
- [ ] No handler touching two databases; `TradingGroupGuardTests` untouched (no new
      cross-company entry point in this task)
- [ ] Task file Work Log complete; `docs/PROGRESS.md` + `docs/CURRENT.md` updated; committed

## Follow-up Findings

- Converge `ICompanyReservationGateway` (TASK-08C-002) onto the shared saga dispatch table when
  07C-004 lands it.
- Empty-company legacy rows invisible under a bound company (catalog, locations, balances,
  identity grants) — needs a 06c backfill decision, not an 08c predicate change. Licensing was
  the only unambiguous exclusion and is fixed; the rest is recorded in PROGRESS.md 2026-09-06.
- `Incoming` carried as zero (no Stage 12 open-PO feed wired); no hosted relay loop (direct
  publish + on-demand rebuild suffice for now).

## Work Log

2026-09-06: Implemented per Scope. Domain/Application/Infrastructure/API/migrations/seed as
specified, with two deviations recorded here: (1) the `(intent_id, leg_id)` idempotency key
gained `sequence_number`, because a hold's own terminal row legitimately shares its intent and
leg and the two-column unique would forbid the close; (2) reservation commands run serialisable
on a service-owned company context rather than in the pipeline transaction (which is
ReadCommitted on the ambient context) — handlers stay thin, architecture exemptions recorded
with reasons. `IStagingQuantityReader` is warehouse-backed (BinType.Staging), not zero-default.
Cross-stage fixes: licensing company-filter exemption (guard-blinding), scoped registry factory
(root-provider tenant bug), optional audit interceptor on `CompanyDbContextFactory`, sync
`IDisposable` on the service. Evidence: unit 995/995, architecture 54/54, integration 466/466
(real PostgreSQL). Full detail in PROGRESS.md session entry 2026-09-06.
