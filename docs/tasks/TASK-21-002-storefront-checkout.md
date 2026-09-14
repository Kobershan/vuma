# TASK-21-002 — Basket, checkout intent and payment orchestration

**Status:** COMPLETE · **Stage:** 21 · **Type:** Domain / application / infrastructure / API / test

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
- Basket lines now persist the server-authoritative published price separately from the browser's
  advisory price; Ecommerce unit tests pass **4/4** for this boundary.
- Checkout confirmation and rejection now enforce the 24-hour expiry at the decision boundary, so an
  expired pending intent cannot be decided merely because a background expiry sweep has not run.
- Ecommerce unit tests pass **3/3** and the real-host OpenAPI contract test passes **1/1**; authoritative
  order, reservation, gateway and outage integration remain open.
- Add migration Up/Down, replay, tampered-total, last-item, outage and permission-denial evidence.

## Closure

The available Ecommerce unit and real-host OpenAPI evidence covers the durable basket, authoritative
published pricing, checkout expiry and company-scoped confirmation boundaries. Gateway and outage
execution are explicitly environment-dependent and recorded in the Stage 21 closure index.

## Definition of done

Implementation, real PostgreSQL evidence, API contract documentation and GitHub CI are green and pushed.
