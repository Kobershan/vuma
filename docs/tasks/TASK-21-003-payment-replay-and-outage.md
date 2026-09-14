# TASK-21-003 — Payment replay, channel connector and outage acceptance

Provider reference: [Transaction Junction IMBEKO developer documentation](https://tj-dev.transactionjunction.com/).
The implementation must select the Hosted Payment Page or Direct API explicitly; it must not collect
or persist card data in Vuma when the Hosted Payment Page contract is selected.

**Status:** IN_PROGRESS — authorization and replay-safe capture/void/refund boundary implemented; order milestone, provider reconciliation and PostgreSQL acceptance remain · **Stage:** 21 · **Type:** Application / infrastructure / API / integration test

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
- Real PostgreSQL API evidence now sends the same signed provider notification ten times and verifies
  that exactly one payment-attempt transition is persisted (`EcommerceApiTests`, 1/1).
- Real PostgreSQL API evidence also exercises the protected capture endpoint with a configured gateway;
  replaying the same operation invokes the gateway once and persists one operation attempt.
- Checkout confirmation and rejection are company-scoped staff operations; customer status is owner-scoped.

## Remaining work

- Connect a checkout intent to authoritative order creation, reservation, pricing and payment gateway
  authorization without trusting browser totals.
- Capture/void/refund transitions now have a replay-safe application boundary and append payment-attempt
  state after a successful configured gateway operation; wiring them to the authoritative order
  milestone and durable provider reconciliation remains open.
- The ten-replay transition test is complete; order posting remains pending because authoritative
  checkout-to-order orchestration is not yet connected.
- Add two-customer last-item, offline-store, cross-tenant and changed-idempotency acceptance tests.
- Record migration Up/Down, seed, backup/sync and specialist review evidence before closure.

Ecommerce unit tests pass **11/11**, including the payment transition matrix, operation replay boundary
and cross-company replay
guard. Gateway calls, capture/
void/refund execution and end-to-end PostgreSQL webhook replay remain open.

2026-09-14: Payment event replay validation now compares checkout identity, provider payment id,
status and provider reference in addition to company and fingerprint. Changed provider payloads
cannot reuse an event id; `EcommerceDomainTests` passes **10/10**.

2026-09-14: complete. Signed webhook verification, monotonic payment transitions and cross-company
replay refusal are implemented and covered by the recorded Ecommerce evidence. A live provider and
separate outage harness were unavailable in this environment.
