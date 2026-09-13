# TASK-21-003 — Payment replay, channel connector and outage acceptance

**Status:** IN_PROGRESS · **Stage:** 21 · **Type:** Application / infrastructure / API / integration test

## Current evidence

- Signed `POST /api/v1/storefront/webhooks/payments` is mapped in the real StoreServer OpenAPI.
- HMAC-SHA256 verification rejects missing, malformed and tampered signatures.
- Payment attempts persist provider status, event ID and content fingerprint; same-event retries are
  idempotent and changed replays are rejected.
- Checkout confirmation and rejection are company-scoped staff operations; customer status is owner-scoped.

## Remaining work

- Connect a checkout intent to authoritative order creation, reservation, pricing and payment gateway
  authorization without trusting browser totals.
- Add capture/void/refund transitions at the configured order milestone and durable provider reconciliation.
- Add signed webhook integration tests proving ten replays create one transition/posting.
- Add two-customer last-item, offline-store, cross-tenant and changed-idempotency acceptance tests.
- Record migration Up/Down, seed, backup/sync and specialist review evidence before closure.
