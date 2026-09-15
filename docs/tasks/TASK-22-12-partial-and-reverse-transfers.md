# TASK-22-12 — Partial fulfillment and reverse transfers

**Depends on:** 22-11

**Status:** COMPLETE (2026-09-15) · **Stage:** 22 · **Type:** Domain / application / API / test

Create cancellable remainder requests and pre-filled reverse transfers as normal compensatable saga work.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

## Current evidence

Remainder and reverse transfers are created as normal related transfer requests with swapped companies
and locations for reversals. A tenant-scoped unique relation index prevents duplicate retries, and the
domain plus migration tests cover the relation metadata.

## Verification

Cancellation is refused after a company-local reservation, while remainder and reverse requests are
created as normal related transfers after receipt. The reservation, shipment and receipt dispatchers
provide the compensating company-ledger paths. Stage 22 registry and migration integration evidence
passes **9/9**, with focused domain coverage for cancellation, partial receipt, remainder and reverse
relation identity.
