# Stage 21b verification evidence

Date: 2026-09-11

Implemented and verified in this workspace:

- Connection codes, mutual connection lifecycle, catalogue publication, price proposal acceptance and rollback.
- Connection-scoped orders with partial confirmation, rejection, dispatch/ASN and receipt.
- Persisted order, remittance and delivery-claim migrations.
- Capture-before-ledger settlement orchestration with idempotent remittance lookup and AP/AR posting boundary.
- Tenant-scoped supplier directory and supplier order-board API.
- Retailer short-delivery/damage/wrong-item claims with supplier credit-note or rejection resolution.
- Replication declarations on Connect orders, remittances and claims; the existing `ReplicationRegistry` discovers all mapped replicated entities from the EF model.
- Payment regulatory boundary documented in `docs/compliance/VUMA-CONNECT-PAYMENTS.md`.

Evidence:

- StoreServer build: 0 errors.
- Focused Connect tests: 11 passed, 0 failed.
- Connect reads resolve through tenant-scoped repositories or an active connection; no endpoint accepts an arbitrary tenant id.

Explicit boundary:

The current repository contains no runnable frontend or Stage 08b design-system package. `VumaRetail.PublicApi` is intentionally a contract library, and supplier portal functionality is therefore exposed as authenticated Connect API routes. A visual portal can only be completed after the frontend/design-system host is added.
