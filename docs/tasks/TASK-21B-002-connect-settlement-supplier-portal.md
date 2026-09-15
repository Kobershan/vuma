# TASK-21B-002 — Connect settlement and supplier portal

Status: IN_PROGRESS — settlement, remittance persistence, supplier portal route, and permission foundations implemented
Stage: 21b
Type: Cross-tenant network, settlement, portal, API

## Objective

Complete supplier-facing Connect trading through confirmation, dispatch, ASN/GRN, remittance and
offline convergence while preserving strict tenant isolation.

## Dependencies

TASK-21B-001; Stage 08b.

## Acceptance criteria

Supplier and retailer APIs must reject unknown order lines, preserve idempotent settlement and
remittance state, prevent cross-tenant reads, and converge after offline replay. Full portal trading,
ASN/GRN and end-to-end settlement evidence remain open.

## Work log

2026-09-15: Existing Connect foundations verified: Connect unit suite 15/15 and PostgreSQL API suite
5/5. Canonical task file created; remaining portal, ASN/GRN and offline-convergence work is retained
as IN_PROGRESS.
