# TASK-17-002 — Production material, output and scrap accounting

**Status:** NOT_STARTED · **Stage:** 17 · **Type:** Domain, application, infrastructure, integration

## Objective

Implement reservation, atomic material issue, finished-output receipt and scrap recording through
the owning company’s stock ledger and existing financial event/posting boundaries.

## Dependencies

TASK-17-001 and Stage 08/08c stock reservation/ledger ports.

## Acceptance criteria

- A shortage refuses material atomically without negative stock.
- Duplicate issues, receipts and scrap operations do not duplicate stock or WIP postings.
- `Ten_units_consume_twenty_components_once` and `Scrap_reconciles_wip` pass against PostgreSQL.
- Every operation is auditable, tenant/company scoped and queued through the shared outbox.

## Work log

- 2026-09-12: canonicalized from Stage 17 part 17-P02. Implementation not yet started.

