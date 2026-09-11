# TASK-22-13 — Transfer delivery note and compliance hook

**Depends on:** 22-10, 22-11

**Status:** IN_PROGRESS · **Stage:** 22 · **Type:** Domain / API / persistence / test

Generate printable/exportable notes with SKU, quantity, batch/expiry, sender, receiver, date and driver reference. Never issue a VAT invoice.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

## Current evidence

Immutable registry delivery-note snapshots now capture transfer identity, sender/receiver, issued time,
driver reference, and line SKU/quantity/location data. Creation is available after shipment through a
protected API route, idempotent by transfer, and its PostgreSQL migration has an executed Up/Down test.

## Remaining work

Printable/export formats and batch/expiry/serial fields remain dependent on the transfer tracking model.
