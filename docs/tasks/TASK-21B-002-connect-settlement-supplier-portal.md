# TASK-21B-002 — Connect settlement and supplier portal

Status: IN_PROGRESS — settlement, remittance persistence, supplier portal access, and permission foundations implemented
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

2026-09-15: Existing Connect foundations verified: Connect unit suite 19/19, PostgreSQL API suite
5/5, and migration suite 20/20. Added explicit supplier portal grants with party-scoped revoke,
tenant-admin-compatible endpoints, and migration `20260915071141_Stage21bSupplierPortalGrants`.
Remaining ASN/GRN integration and offline-convergence work is retained as IN_PROGRESS.
