# TASK-21B-002 — Connect settlement and supplier portal

Status: IN_PROGRESS — settlement, party-scoped remittance read, supplier portal access, and permission foundations implemented
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

2026-09-15: Existing Connect foundations verified: Connect unit suite 20/20, PostgreSQL API suite
5/5, and migration suite 20/20. Added explicit supplier portal grants with party-scoped revoke,
tenant-admin-compatible endpoints, and migration `20260915071141_Stage21bSupplierPortalGrants`.
Added purchase-order-line links to Connect order lines, an ASN-to-draft-GRN command and endpoint,
and a PostgreSQL migration `20260915071947_Stage21bConnectAsnPurchaseOrderLink`; ASN shipment lines
now also persist batch, serial, expiry and package references via
`20260915072953_Stage21bConnectAsnShipmentDetails`. The retailer must still explicitly complete the
resulting GRN. Offline-convergence work remains IN_PROGRESS.

2026-09-15: Added the party-scoped remittance query and `GET /api/v1/connect/payments/{paymentId}/remittance`.
The retailer and supplier can now retrieve the persisted settlement advice while unrelated tenants receive
no result. The Connect unit suite passes 21/21 and the Web Release build has 0 errors. End-to-end portal
trading, full settlement integration, isolation sweep and offline convergence remain open.

2026-09-15: Added `GET /api/v1/connect/connections/{id}/granted-users`, returning only grants for
the requested connection when the caller is one of its two parties. The Connect unit suite passes
22/22 and Web Release remains green with 0 errors. Full portal trading and offline convergence remain open.
