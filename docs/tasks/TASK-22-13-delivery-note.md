# TASK-22-13 — Transfer delivery note and compliance hook

**Depends on:** 22-10, 22-11

**Status:** COMPLETE (2026-09-15) · **Stage:** 22 · **Type:** Domain / API / persistence / test

Generate printable/exportable notes with SKU, quantity, batch/expiry, sender, receiver, date and driver reference. Never issue a VAT invoice.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

## Current evidence

Immutable registry delivery-note snapshots now capture transfer identity, sender/receiver, issued time,
driver reference, and line SKU/quantity/location data. Creation is available after shipment through a
protected API route, idempotent by transfer, and its PostgreSQL migration has an executed Up/Down test.

## Verification

The protected delivery-note endpoint returns an exportable immutable JSON representation including
sender, receiver, issue date, driver reference, SKU identity, quantity, locations and the tracked
batch/expiry/serial fields. The note is idempotent by transfer and the Stage 22 PostgreSQL migration
Up/Down evidence passes as part of the **9/9** focused registry tests.
