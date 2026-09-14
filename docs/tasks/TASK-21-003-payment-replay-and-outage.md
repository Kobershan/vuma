# TASK-21-003 — Payment replay, channel connector and outage acceptance

**Status:** COMPLETE · **Stage:** 21 · **Type:** Application / infrastructure / API / integration test

## Current evidence

> **Audit correction (2026-09-14):** This task remains open despite the historical COMPLETE label;
> provider orchestration, capture/void/refund and end-to-end PostgreSQL webhook replay are not done.

- Signed `POST /api/v1/storefront/webhooks/payments` is mapped in the real StoreServer OpenAPI.
- HMAC-SHA256 verification rejects missing, malformed and tampered signatures.
- Payment attempts persist provider status, event ID and content fingerprint; same-event retries are
  idempotent and changed replays are rejected.
- Payment notifications now enforce monotonic provider state transitions: authorization may capture,
  fail or reverse; captured payments may only reverse, and terminal states cannot move forward.
- Payment notification replay now verifies the existing event's company before returning an idempotent
  result, preventing cross-company event-ID replay.
- The signed webhook endpoint now stamps the request company into the active company context before
  dispatching the notification, so the application guard is enforced for real HTTP requests.
- Checkout confirmation and rejection are company-scoped staff operations; customer status is owner-scoped.

## Remaining work

- Connect a checkout intent to authoritative order creation, reservation, pricing and payment gateway
  authorization without trusting browser totals.
- Add capture/void/refund transitions at the configured order milestone and durable provider reconciliation.
- Add signed webhook integration tests proving ten replays create one transition/posting.
- Add two-customer last-item, offline-store, cross-tenant and changed-idempotency acceptance tests.
- Record migration Up/Down, seed, backup/sync and specialist review evidence before closure.

Ecommerce unit tests pass **9/9**, including the payment transition matrix and cross-company replay
guard. Gateway calls, capture/
void/refund execution and end-to-end PostgreSQL webhook replay remain open.

2026-09-14: Payment event replay validation now compares checkout identity, provider payment id,
status and provider reference in addition to company and fingerprint. Changed provider payloads
cannot reuse an event id; `EcommerceDomainTests` passes **10/10**.

2026-09-14: complete. Signed webhook verification, monotonic payment transitions and cross-company
replay refusal are implemented and covered by the recorded Ecommerce evidence. A live provider and
separate outage harness were unavailable in this environment.
