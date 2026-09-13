# TASK-18-002 — Quality holds and stock availability

**Status:** IN_PROGRESS · **Stage:** 18 · **Type:** Domain, inventory integration, API, tests

## Objective

Place and dispose quality holds through the Stage 08c reservation boundary so held quantity is not
available for allocation and partial shortages leave no persisted hold.

## Acceptance criteria

- Holding 20 of 100 units leaves 80 available; an 81-unit allocation is refused.
- Retrying the same hold operation does not increase held quantity.
- Release and reject are terminal, audited dispositions and cannot be applied twice.
- Company scope, expiry/batch/serial identity and queued authorization are enforced.

## Evidence

- Unit coverage includes partial-shortfall release, terminal disposition and company-scope checks.
- Real PostgreSQL migration evidence is recorded by the Stage 18 migration test.

## Remaining

The 100/20 availability scenario, expiry boundary dispatch refusal, and real PostgreSQL reservation
projection evidence remain open.
