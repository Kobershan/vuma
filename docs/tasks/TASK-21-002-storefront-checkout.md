# TASK-21-002 — Basket, checkout intent and payment orchestration

**Status:** IN_PROGRESS · **Stage:** 21 · **Type:** Domain / application / infrastructure / API / test

## Objective

Complete the storefront transaction boundary after catalogue publication: durable basket ownership,
idempotent checkout intents, store-authoritative confirmation, and explicit payment outcomes.

## Current evidence

- Tenant/company/channel-scoped `CommerceBasket` foundation and reversible EF migration are pushed.
- `POST /api/v1/storefront/baskets` is registered in OpenAPI and the focused PostgreSQL API test is green.

## Remaining acceptance work

- Basket lines must reference published product versions; browser prices remain advisory.
- Checkout requires `Idempotency-Key`, stores a content fingerprint, expires after 24 hours, and returns
  `202 Accepted` until the owning store confirms.
- Store confirmation must use the existing Orders, Inventory, Sales and Finance ports; no false stock or
  payment promise is allowed during an outage.
- Payment notification recording now uses stable event IDs, payload fingerprints and signed webhook
  verification; provider authorization/capture/void/refund orchestration and store order settlement remain.
- Add migration Up/Down, replay, tampered-total, last-item, outage and permission-denial evidence.

## Definition of done

Implementation, real PostgreSQL evidence, API contract documentation and GitHub CI are green and pushed.
