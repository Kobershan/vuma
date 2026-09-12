# TASK-17-001 — Production-order lifecycle and BOM snapshot

**Status:** IN_PROGRESS · **Stage:** 17 · **Type:** Domain, application, persistence, tests

## Objective

Implement the traceable production-order aggregate and release-time snapshot of the published BOM,
routing, requested quantity and cost basis. A later BOM revision must not alter a released order.

## Scope

Add the domain model, repository port, create/release commands, EF mapping/migration and focused
unit/integration tests. Commands must carry side-effect and entitlement classification, tenant/company
scope, client operation identity and deterministic validation.

## Acceptance criteria

- Draft → Released → InProgress → Completed → Closed is enforced; illegal transitions are rejected.
- Release requires a published BOM and stores an immutable snapshot.
- Duplicate create/release operation IDs are idempotent; changed content for an existing operation is
  rejected.
- Tenant/company scope is enforced and all state changes use the transaction/outbox boundary.
- Migration Up/Down and real PostgreSQL integration evidence are recorded.

## Tests required

`Bom_changes_do_not_reprice_released_order`, lifecycle transition tests, tenant/company denial,
duplicate operation tests, migration reversibility and architecture classification tests.

## Work log

- 2026-09-12: canonicalized from Stage 17 part 17-P01. Implementation not yet started.
- 2026-09-12: added the caller-identified `ProductionOrder` lifecycle and immutable release-time BOM
  snapshot/material requirement model in `src/VumaRetail.Domain/Manufacturing/ProductionOrder.cs`.
  Focused manufacturing unit suite passes 16/16 including three production-order tests. Persistence,
  command handlers, migration and PostgreSQL evidence remain open; task is not complete.
