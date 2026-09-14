# TASK-18-002 — Quality holds and stock availability

**Status:** IN_PROGRESS — dispatch gate implementation added; PostgreSQL boundary evidence remains · **Stage:** 18 · **Type:** Domain, inventory integration, API, tests

## Objective

> **Audit correction (2026-09-14):** Dispatch enforcement is now implemented in
> `ShipWaveCommandHandler` and runs before stock moves into dispatch. Source-built and PostgreSQL
> boundary evidence remain to be executed.

Place and dispose quality holds through the Stage 08c reservation boundary so held quantity is not
available for allocation and partial shortages leave no persisted hold.

## Acceptance criteria

- Holding 20 of 100 units leaves 80 available; an 81-unit allocation is refused.
- Retrying the same hold operation does not increase held quantity.
- Release and reject are terminal, audited dispositions and cannot be applied twice.
- Company scope, expiry/batch/serial identity and queued authorization are enforced.

## Evidence

- Unit coverage includes partial-shortfall release, terminal disposition and company-scope checks.
- Unit coverage now also rejects releasing a hold whose persisted expiry date is at or before the
  current UTC business date; quality-focused unit tests pass **11/11**.
- Real PostgreSQL migration evidence is recorded by the Stage 18 migration test.
- Real PostgreSQL API evidence now covers the core availability invariant: holding 20 from 100
  leaves 80 available, an 81-unit hold is refused without changing the projection, and retrying
  the same operation returns the original hold without increasing held quantity (`QualityApiTests`
  focused scenario, 2026-09-13). The shortfall is mapped through the domain rule
  `QUALITY_HOLD_EXCEEDS_AVAILABLE` to HTTP 422 rather than an unhandled 500.

## Remaining

The real PostgreSQL expiry-boundary dispatch refusal and reservation projection evidence remain open;
the application gate now checks both active quality holds and expired tracked stock at the supplied
business date.

## Work log

- 2026-09-13: Added the real PostgreSQL API scenario for the 100/20 hold boundary, idempotent retry,
  and atomic 81-unit shortfall refusal. The shortfall initially surfaced as HTTP 500; introduced
  the typed `QUALITY_HOLD_EXCEEDS_AVAILABLE` domain rule so the central API error contract returns
  HTTP 422 without persisting a hold. Focused integration evidence is now 13/13 green; expiry and
  dispatch integration remain open.
