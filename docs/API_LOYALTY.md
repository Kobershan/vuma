# API_LOYALTY — Vuma Loyalty Public API Contract

> The authoritative wire shape of every public loyalty endpoint (Stage 20). ADR-021: this host's
> DTOs are its own — cost, margin, supplier and other-customer data are structurally unreachable,
> not filtered. Served today by the store server via `MapVumaLoyaltyPublic`; the standalone
> API-key/member-token deployable follows with Stage 30b's auth (ADR-153).

Base path: `/api/v1/loyalty`. Versioned per `docs/API_STANDARDS.md` §2. Every response carries
`X-Vuma-Api-Version: v1`. Errors are RFC 7807 problem documents with a stable `code`.

## Authentication

| Caller | Credential (v1) | Scope |
|---|---|---|
| Till / staff | JWT bearer + `loyalty.*` permission | Whatever the permission grants |
| Member (future) | Member token carrying a `vuma_customer` claim | Own record only; anything else is `404` |
| Orbit | HMAC-SHA256 (`X-Orbit-Signature: sha256=<hex>` of the raw body) | Webhooks only |

Member tokens and OAuth client credentials are Stage 30b's. Until then, member endpoints are
staff-mediated and the member-scope filter is exercised by tests.

## Idempotency

Every `POST` that moves value takes an `Idempotency-Key` header (UUID v7, client-generated).
Same key + same body returns the original outcome; same key + different body is `409
LOYALTY_DUPLICATE_KEY`; same key while the original is in flight is `409
LOYALTY_REQUEST_IN_PROGRESS`. Completed replays do not count against rate limits.

## Rate limits

Fixed-window per caller: tills 100/min, members 30/min. Over the limit is `429` with a
`Retry-After` header in seconds. Counters are per node (a store deployment); multi-node
cloud moves them to Redis with identical limits and headers (Stage 30b).

## Read-only

Reads always serve. Writes while the tenant is read-only answer `503` with code
`LOYALTY_TEMPORARILY_UNAVAILABLE` and no billing signal — the body never contains the words
subscription, licence, lapsed, billing or payment (asserted in test).

## Endpoints

### Members

| Method | Path | Auth | Success | Notes |
|---|---|---|---|---|
| `POST` | `/members/{customerId}/enroll` | `loyalty.member.enroll` | `201` member | `{companyId, storeId?}`. Already enrolled in this company → `409 LOYALTY_ALREADY_ENROLLED` |
| `GET` | `/members/{customerId}` | `loyalty.member.view` | `200` member | Balance cache labelled with `balanceAsAt` + `balanceStale` |
| `POST` | `/members/{customerId}/earn` | `loyalty.earn.issue` | `200` earn, or `202` when queued | `{companyId, purchaseAmount, currency, reference?, idempotencyKey, isMarketingBonus?}`. Marketing bonuses need `MarketingEmail` consent (`422 CONSENT_NOT_GIVEN`) |
| `POST` | `/members/{customerId}/redeem` | `loyalty.redeem.issue` | `200` redeem, or `202` when queued | `{companyId, points, reference?, idempotencyKey}`. Cache pre-check is provisional; Orbit's debit is authoritative (`422 INSUFFICIENT_POINTS`) |
| `GET` | `/members/{customerId}/balance` | `loyalty.balance.view` | `200` balance | Cache only, with `asAt` + `isStale`. Stale reads say so: show "may be updated" |
| `GET` | `/members/{customerId}/transactions` | `loyalty.balance.view` | `200` list | `?companyId&limit`, newest first |
| `GET` | `/members/{customerId}/tier` | `loyalty.member.view` | `200` tier progress | Current tier + next tier + points-to-next, off the cache |

A `202` means accepted-for-later-work: the till serves the customer now, the retry worker
confirms within minutes, and the transaction history shows the outcome. Nothing is ever
silently dropped.

### Catalogue

| Method | Path | Auth | Success | Notes |
|---|---|---|---|---|
| `GET` | `/tiers?companyId` | `loyalty.catalogue.view` | `200` list | Threshold ascending |
| `GET` | `/rewards?companyId&tierId?` | `loyalty.catalogue.view` | `200` list | Cost and availability re-verify at redemption |
| `POST` | `/catalogue/sync` | `loyalty.admin.manage` | `200` counts | `{companyId}`. Pulls Orbit's tiers + rewards into the cache; Orbit down → `503 LOYALTY_ORBIT_UNAVAILABLE` |
| `POST` | `/settings` | `loyalty.admin.manage` | `204` | `{companyId, currency, earnRate, pointExpiryDays, enabled}` |

### Webhooks

| Method | Path | Auth | Success | Notes |
|---|---|---|---|---|
| `POST` | `/webhooks/notifications` | HMAC, anonymous | `202` | `{companyId, orbitMemberId, balance?, tierId?, eventType}`. Unknown members are ignored — a webhook never conjures a member. Bad signature → `401` |

## Error codes

`LOYALTY_DISABLED` (422) · `LOYALTY_MEMBER_NOT_FOUND` (404) · `LOYALTY_ALREADY_ENROLLED` (409) ·
`INSUFFICIENT_POINTS` (422) · `INVALID_POINTS` (422) · `LOYALTY_DUPLICATE_KEY` (409) ·
`LOYALTY_REQUEST_IN_PROGRESS` (409) · `LOYALTY_TRANSACTION_NOT_FOUND` (404) ·
`CONSENT_NOT_GIVEN` (422) · `LOYALTY_ORBIT_UNAVAILABLE` (503) ·
`LOYALTY_TEMPORARILY_UNAVAILABLE` (503, neutral) · `LOYALTY_RATE_LIMITED` (429) ·
`LICENCE_MODULE_NOT_ENABLED` (403, unlicensed tenant).

## Worked example

```
POST /api/v1/loyalty/members/{id}/earn          Idempotency-Key: <uuid-v7>
{ "companyId": "<c>", "purchaseAmount": 150.00, "currency": "ZAR",
  "reference": "sale-0193", "idempotencyKey": "<uuid-v7>" }

200  { "transactionId": "<t>", "points": 150.0, "newBalance": 1150.0,
       "tierId": "silver", "queued": false }
— or, engine unreachable —
202  { "transactionId": "<t>", "points": 150.0, "newBalance": 1000.0,
       "tierId": "silver", "queued": true }
```

Replaying the same key returns the first document byte-for-byte in effect; the ledger moves once.
