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
- 2026-09-12: added the tenant/company-scoped repository, commands, EF configuration and reversible
  `20260912215016_Stage17_ProductionOrderLifecycle` migration. Infrastructure build passes with 0
  errors; local PostgreSQL migration execution is not available because the configured `vuma` user
  password is rejected. The preceding pushed commit `ef12f02` is green in CI run `34720055190`.
- 2026-09-13: restored the durable terminal lockout/webhook migration chain in `aa96dcb`; CI run
  `34721773841` has passed Build, Architecture, Vulnerability, Test, Design System and Migration
  gates, with the Windows Package job still queued. Added the requested BOM identity to production
  orders, company assignment for newly-created BOMs, changed-payload rejection for duplicate create
  operations, and migration `20260913050239_Stage17_ProductionOrderRequestIdentity`. Focused
  manufacturing tests pass 17/17. Material/output integration and API/closure work remain open, so
  this task and Stage 17 are intentionally not marked complete.
- 2026-09-13: aligned the production API with location-scoped stock operations in `f028ff9` and
  made release replay-safe by persisting the release operation identity in the BOM snapshot. The
  focused manufacturing unit suite now passes 19/19 and the Web project builds with 0 errors. The
  required full CI workflow for `f028ff9` is still running; PostgreSQL migration and tenant-scope
  acceptance evidence remain open.
- 2026-09-13: CI run `34743485288` passed all required gates, including migration, architecture,
  vulnerability, full test and Windows/Android packaging jobs. Release replay coverage is now 19/19
  focused manufacturing tests; tenant/company PostgreSQL acceptance remains open.
