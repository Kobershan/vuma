# TASK-22-12 — Partial fulfillment and reverse transfers

**Depends on:** 22-11

**Status:** IN_PROGRESS · **Stage:** 22 · **Type:** Domain / application / API / test

Create cancellable remainder requests and pre-filled reverse transfers as normal compensatable saga work.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

## Current evidence

Remainder and reverse transfers are created as normal related transfer requests with swapped companies
and locations for reversals. A tenant-scoped unique relation index prevents duplicate retries, and the
domain plus migration tests cover the relation metadata.

## Remaining work

Cancellation/reversal saga execution and full company-ledger integration tests remain.
