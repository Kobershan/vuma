# TASK-22-10 — Batch-aware pick, ship and in-transit bucket

**Depends on:** 22-09, 13

Carry batch, expiry and serial identities. Fix transfer cost at ship time; exclude InTransit from both parties on-hand.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

## Current evidence

Transfer lines now carry optional batch/lot, expiry-date and serial identities. Serialised lines are
restricted to quantity one, duplicate serials are rejected within a transfer at the database level,
and multiple lines for the same SKU are allowed when their tracking identities differ. The identities
are copied into related transfers, delivery-note snapshots and the durable reservation/shipment/receipt
saga payloads. The reversible PostgreSQL migration and domain tests are included in the Stage 22 suite.

## Remaining work

The company-local stock ledger still needs native batch/serial columns and reservation/valuation logic
before these identities can constrain stock selection rather than only travel with the registry saga.
