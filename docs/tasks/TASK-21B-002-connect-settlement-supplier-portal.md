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

2026-09-15: ASN-to-GRN creation now treats the supplier delivery-note number as an idempotency key
within the purchase order, so a dropped response cannot create a second draft receipt. Explicit receipt
IDs remain supported for clients that persist operation identity. Web Release and Connect unit tests remain green.

2026-09-15: Added `GET /api/v1/connect/connections/{id}/granted-users`, returning only grants for
the requested connection when the caller is one of its two parties. The Connect unit suite passes
22/22 and Web Release remains green with 0 errors. Full portal trading and offline convergence remain open.

2026-09-15: Added real PostgreSQL persistence coverage for supplier portal grants. Supplier and
retailer parties can read the connection's grant while an unrelated tenant receives an empty result;
the focused Connect persistence suite passes **2/2**. Full portal trading and offline convergence
remain open.

2026-09-15: Added real HTTP API coverage for a retailer reading a connection's supplier portal
grant. The API/OpenAPI/permission suite passes **3/3** and the integration project builds with 0
errors. Full portal trading, ASN/GRN completion, settlement isolation and offline convergence remain open.

2026-09-15: Added a real HTTP replay test for a dispatched ASN creating a draft goods receipt. Two
identical requests return Created while exactly one receipt and receipt line persist. The Connect API
suite passes **4/4** and the integration project builds with 0 errors. Explicit GRN completion and
offline convergence remain open.

2026-09-15: Added real HTTP settlement replay coverage using the configured provider seam. Repeated
settlement returns the same durable remittance and the connected retailer can read it through the
remittance endpoint. The focused settlement test passes **1/1**; supplier-side and cross-tenant
settlement isolation plus offline convergence remain open.
