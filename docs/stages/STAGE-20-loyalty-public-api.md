# STAGE 20 — Loyalty Programme & Public API ★

**Status:** COMPLETE (2026-09-10) · **Depends on:** 10, 19 · **Reference reading:** `docs/DATA_MODEL.md` §1–§3, `docs/CONVENTIONS.md` §1–§5, `docs/TESTING.md` §3, §4, `docs/API_STANDARDS.md` (all), `docs/SECURITY.md` §1, §4, `docs/LICENSING.md` §1–§4, `docs/DECISIONS.md` ADR-021, ADR-031, **ADR-112** (snapshot pricing), ADR-129 (no LLM data access), `docs/API_LOYALTY.md` (the public loyalty API contract — the authoritative shape of every endpoint documented below), `docs/PROGRESS.md` §4.14.

## Task index

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 20-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | COMPLETE |
| [TASK-20-001](../tasks/TASK-20-001-loyalty-vertical.md) | DOMAIN / APPLICATION / INFRASTRUCTURE / API | Loyalty vertical | Stages 10, 19 | COMPLETE |
| [TASK-20-002](../tasks/TASK-20-002-public-api-verification.md) | TEST / DOCUMENTATION | Public API verification | TASK-20-001 | COMPLETE |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in `docs/tasks/README.md`.

## Objective

Stage 20 builds **Vuma's front-facing loyalty experience** — till, online, and mobile — that lets customers earn and burn loyalty points, and the **public-facing API** that exposes loyalty to Vuma's own front ends and third-party consumers. Vuma does **not** build a competing loyalty engine. The actual earn/burn/tiers/rewards logic and the **points ledger** live in **Proxima Orbit**, an existing loyalty SaaS with its own POS API. Orbit is vendored into the Vuma repo as a first-class dependency. Vuma's job is to integrate with it tightly.

**The model in one line:** Vuma owns the experience and the integration surface; Orbit owns the engine and the ledger.

This separation is structural, not rhetorical:

| Concern | Owner |
|---|---|
| Till UI, mobile UI, web UI | **Vuma** |
| Member onboarding, profile UI | **Vuma** |
| Earn/burn request routing, idempotency, error handling | **Vuma** |
| Tier display, rewards catalogue UI | **Vuma** |
| Points calculation, accrual, redemption, expiry | **Orbit** |
| Tier evaluation, reward fulfilment | **Orbit** |
| Points ledger (source of truth) | **Orbit** |
| Orbit's own internal business logic | **Orbit** |
| Vuma's public API surface, auth, rate limiting | **Vuma** |

### What this stage does **not** own

- **The loyalty engine or points ledger.** Owned by Proxima Orbit. Vuma never computes points, tier eligibility, or reward fulfilment.
- **Orbit's internal domain model.** Vuma does not duplicate Orbit's entities — only the view it needs to render the front end and route requests.
- **Any computation that Orbit performs.** A model with a repository or a `DbContext` that computes loyalty figures is a defect per ADR-129 — the assistant classifies and phrases; it never computes.
- **Customer identity.** Owned by Stage 06 (`catalog.Customer`), maintained by Stage 19 (CRM). Stage 20 consumes it.
- **Segment membership and consent.** Owned by Stage 19. Stage 20 consumes `IsConsentValidAsync` to gate marketing-facing loyalty actions.
- **The public storefront API** (product catalog, cart, checkout). That is Stage 21, which depends on Stage 20's member identity.

## Dependencies

### Stage 10 (Sales & Promotions) — provides

- **Discount context** — a loyalty redemption is a kind of discount. Stage 10's `PromotionEngine` extension point (see `docs/stages/STAGE-10-sales-promotions.md` line 228) lets a loyalty reward act as a promotion whose applicability test asks about a member. Stage 20 consumes this extension point; it does not replace it.
- **Price resolution awareness** — a redeemed reward must appear in the price-resolution explanation (`PriceResolution.Explanation`).

### Stage 19 (CRM) — provides (the critical contract)

Stage 19 is a hard dependency. Stage 20 cannot determine who a member is, what segment they belong to, or whether they have consented to marketing communications without Stage 19.

| Stage-19 contract | Type | Stage-20 use |
|---|---|---|
| `Customer.id` (UUID) | `catalog.Customer` record | Every loyalty operation is scoped to a customer. |
| `ILeadService`, `IOpportunityService`, `IActivityService` | Internal ports | Not directly consumed at v1; the identity link is via `Customer.id`. |
| `ISegmentService.IsMemberAsync(segmentId, customerId, ...)` | Internal port | Determine tier eligibility (e.g. "Gold members = customers in the Gold segment"). |
| `IConsentService.GetConsentStateAsync(customerId, ConsentType)` | Internal port | **Gates marketing-facing loyalty actions.** If `MarketingEmail` consent is not `Given`, no loyalty marketing message may be sent. If `DataProcessing` consent is not `Given`, the member's data must not leave the store (critical for cross-border sync to cloud). |
| `ICustomer360ViewService.GetViewAsync(customerId)` | Internal port | The till and mobile app show the 360° view including loyalty balance. |
| `IConsentService.IsConsentValidAsync(customerId, ConsentType)` | Internal port | Quick boolean for "may we send this loyalty notification?" |

**Stage 19 does not exist yet** (no `docs/stages/STAGE-19-crm.md` on main — `docs/ROAPMAP.md` lists Stage 19 as CRM but the document is not present). This document assumes the contract defined in `docs/stages/STAGE-19-crm.md` (written in this session) will match the final implementation. Any divergence must be resolved before Stage 20 begins.

### Stage 07 (Finance) — provides

- **Money handling** — all loyalty monetary values (reward value, tier fees) use `decimal(18,4)` with currency code. Stage 20 does not post to GL; it raises financial events through `IFinancialEventPoster` (Stage 07's pattern).
- **Document numbering** — reward fulfilment documents follow ADR-065 numbering sequences.

### Stage 04b (Licensing) — provides

- **Entitlement flag** — loyalty module must be licensed (`entitlement.loyalty`). An unlicensed tenant may not use loyalty features; endpoints return `403 LICENCE_MODULE_NOT_ENABLED`.
- **Read-only enforcement** — a lapsed subscription drops the tenant to read-only. Per `docs/TESTING.md` §7: "the public storefront and loyalty APIs still serve reads, and their writes return a neutral `503` that does not disclose the tenant's billing status."
- **Metering** — daily loyalty counters (members enrolled, points earned, points burned, tier upgrades) follow the whitelist pattern in `docs/TESTING.md` §7.

### Vendored Proxima Orbit — provides

- **Points engine** — accrual, redemption, expiry.
- **Tier engine** — tier evaluation and progression.
- **Points ledger** — the source-of-truth record of every point movement.
- **Reward fulfilment** — what happens when points are redeemed.
- **Orbit's own API** — HTTP/JSON or gRPC, versioned, with its own auth model.

See §Vendoring approach below.

## Vendoring approach

### Where Orbit lives in the repo

**Proposed path: `vendor/ProximaOrbit/`**

Justification:
- `src/` is reserved for first-party Vuma projects (`VumaRetail.*`), each with a matching `.csproj` and solution reference (ADR-042: "the product is Vuma Retail; code is `VumaRetail.*`, repo and folder are `vuma`").
- Orbit is a third-party product, not first-party code. It should not live under `src/`.
- `vendor/` is the conventional location for vendored dependencies across the industry (e.g. `.NET`'s implicit `vendor/`, npm's `vendor/`, Go's `vendor/`).
- No `vendor/` directory currently exists in this repo — this proposal establishes the convention.
- A separate `vendor/ProximaOrbit/` keeps the boundary explicit: anything in `vendor/` is third-party code Vuma integrates with but does not own; anything in `src/` is Vuma-owned.

**Full proposed structure:**

```
vuma/
├── src/                          ← first-party Vuma projects (VumaRetail.*)
│   ├── VumaRetail.Domain/
│   ├── VumaRetail.Application/
│   ├── VumaRetail.Infrastructure/
│   ├── VumaRetail.PublicApi/     ← Stage 20's new project (or sub-project under an existing host)
│   └── ...
├── vendor/
│   └── ProximaOrbit/             ← vendored loyalty engine
│       ├── ProximaOrbit.Api/     ← Orbit's own API (as Vuma's dependency)
│       ├── ProximaOrbit.Ledger/  ← points ledger
│       ├── ProximaOrbit.Tiers/   ← tier engine
│       ├── ProximaOrbit.Engine/  ← earn/burn core
│       ├── ProximaOrbit.Abstractions/  ← interfaces Vuma calls
│       └── ProximaOrbit.sln      ← Orbit's own solution (or included in VumaRetail.sln)
├── tests/
│   └── ...
└── VumaRetail.sln                ← updated to include vendor projects if compiled in
```

### How Orbit is kept in sync / updated over time

**Chosen approach: subtree merge** — justified as follows:

| Approach | Why rejected |
|---|---|
| Manual copy | Unmaintainable; diverges silently; no diff history. |
| Git submodule | Submodules are notoriously difficult for the team; they add a layer of complexity that the autonomous build sessions cannot manage reliably (CLAUDE.md §1: unattended sessions). Also, submodules pin a commit — updating requires a deliberate `submodule update --remote` that the automated pipeline won't do. |
| **Subtree merge** | **Chosen.** Orbit's code is physically in the Vuma repo under `vendor/ProximaOrbit/` but tracked as a subtree. Updates from Orbit's upstream are pulled via `git subtree pull --prefix=vendor/ProximaOrbit ...`. The Vuma repo has full history of Orbit changes. No submodule overhead. Merge conflicts are resolved like any other code merge. The `vendor/` directory is part of the repo's normal git flow. |

**Update cadence:** Orbit updates are pulled when a new Orbit version is validated by the Vuma team. The `vendor/ProximaOrbit/` directory has a `VERSION` file and a `CHANGELOG.md` symlink or reference so the team knows what version is vendored.

### How Vuma's build treats Orbit

**Chosen: compiled in, same build, same CI.**

Justification:
- A separate network service would introduce a runtime dependency on a separate deployment, a separate CI pipeline, and a separate versioning cadence — all of which contradict Stage 20's requirement that the integration be tight and easy.
- Compiling Orbit into the same `VumaRetail.sln` means the full build includes Orbit, the integration tests can reference Orbit types directly, and there is no network hop in integration tests.
- Orbit's own API is still called over HTTP/gRPC within the same process (localhost loopback) — this mirrors the production topology where Vuma's integration layer calls Orbit's API over the network, but in dev/test it's loopback.
- **Production topology:** Vuma's `PublicApi` host calls Orbit's API over HTTP/mTLS (same as any third-party integration). The compiled-in relationship is for build-time convenience and testability; runtime is still a service boundary.
- **Alternative rejected — separate service:** would require managing two deployable units, two sets of certificates, two CI pipelines, and a network-level failure mode that complicates the failure-mode tests §Failure-mode tests requires. The compiled-in + HTTP-at-runtime hybrid gives the best of both worlds.

**Build treatment:**
- `vendor/ProximaOrbit/` projects are referenced by `VumaRetail.PublicApi` and test projects.
- `VumaRetail.sln` includes Orbit projects.
- Architecture tests (Stage 00) are extended to assert that `vendor/ProximaOrbit/` code follows Vuma's conventions where Vuma extends Orbit (not the reverse — Vuma may add wrappers, but never modifies Orbit's core).
- Any modification to vendored Orbit code must be accompanied by a changelog entry and a test that the modification doesn't break Orbit's own API contract.

## Domain model

Vuma does **not** duplicate Orbit's full domain model. Vuma stores only what it needs to render the front-facing UI and route requests. Orbit's ledger is authoritative; Vuma's local cache is eventually consistent and reconcilable.

### Vuma's local model

| Vuma entity | Schema | Fields | Source of truth |
|---|---|---|---|
| `LoyaltyMember` | `loyalty` | `CustomerId` (PK, FK to `catalog.Customer`), `OrbitMemberId` (string, Orbit's member identifier), `EnrolledAt`, `TierId` (cached), `BalanceCache` (`decimal(18,4)`), `BalanceCacheAsAt` (timestamptz), `LastSyncAt`, `StoreId` | **Hybrid:** `CustomerId`, `EnrolledAt`, `OrbitMemberId` are Vuma's; `BalanceCache` is Vuma's local cache; `TierId` is cached from Orbit. |
| `LoyaltyTransaction` | `loyalty` | `Id` (UUID v7), `CustomerId`, `OrbitTransactionId` (string, Orbit's transaction id), `TransactionType` (`Earn`, `Burn`, `Adjustment`, `Expiry`), `Amount` (`decimal(18,4)`), `RunningBalance`, `Reference` (e.g. sale ID), `OccurredAt`, `SyncedAt`, `StoreId` | **Vuma-side event log.** Every earn/burn produces a `LoyaltyTransaction` row in Vuma's DB *before* calling Orbit (for idempotency). Orbit's ledger is the true balance; Vuma's `RunningBalance` is a projection reconciled against Orbit on sync. |
| `LoyaltyTier` | `loyalty` | `TierId` (string, matches Orbit's tier identifier), `Name`, `DisplayName`, `ImageUrl`, `EffectiveFrom`, `EffectiveTo`, `StoreId` | **Cached from Orbit.** Tier definitions are pulled from Orbit and cached locally for fast rendering. Updated via sync. |
| `LoyaltyReward` | `loyalty` | `RewardId` (string, matches Orbit's), `TierId`, `Name`, `Description`, `CostInPoints`, `ImageUrl`, `Availability`, `StoreId` | **Cached from Orbit.** The rewards catalogue is rendered locally; availability and cost are verified against Orbit at redemption time. |
| `LoyaltySettings` | `loyalty` | `TenantId`, `EnableLoyalty`, `EarnRate` (points per ZAR), `TierThresholds` (cached), `PointExpiryDays`, `StoreId` | **Vuma config.** Earn rate and expiry are Vuma's configuration; tier thresholds may be cached from Orbit. |

### What Vuma never stores

- **The points ledger itself.** Every point movement lives in Orbit's ledger. Vuma's `LoyaltyTransaction` is an event log, not a ledger.
- **Tier eligibility calculation.** Vuma caches the tier result; Orbit evaluates it.
- **Reward fulfilment state.** Orbit manages fulfilment; Vuma tracks that a fulfilment was requested.

### Cache consistency model

`BalanceCache` is **eventually consistent** against Orbit's ledger. Vuma trusts its local cache for display and fast till lookups, but any balance-critical operation (burn/redemption) validates against Orbit in real time. Reconciliation runs periodically (a `ILoyaltyReconciliationService` that compares Vuma's `RunningBalance` against Orbit's reported balance and logs discrepancies).

## Earn/burn flow

### Earn flow (e.g. a POS sale)

```
POS (till) ──► Vuma PublicApi POST /v1/loyalty/members/{id}/earn
                  │
                  ▼
          Vuma API layer:
          1. Validate customer exists (Stage 06/19)
          2. Check entitlement (Stage 04b)
          3. Check consent (Stage 19) — if marketing-related earn, gate on consent
          4. Create LoyaltyTransaction (type=Earn, status=Pending)
             — idempotency key generated here
          5. Call Orbit: POST /orbit/v1/members/{orbitMemberId}/earn
             Body: { amount, reference, idempotencyKey }
          6. On Orbit 200 OK: update LoyaltyTransaction (status=Confirmed, orbitTransactionId)
             Update BalanceCache (eventual — cached)
          7. On Orbit timeout/unreachable: status=QueuedForRetry, enqueue retry
          8. Return to POS
```

### Burn/redeem flow (e.g. a customer redeems points for a discount)

```
POS ──► Vuma PublicApi POST /v1/loyalty/members/{id}/redeem
           │
           ▼
     Vuma API layer:
     1. Validate customer, entitlement, consent
     2. Check BalanceCache ≥ requested points (fast local check)
        — If insufficient, refuse immediately with 422 INSUFFICIENT_POINTS
        — This is a *cache*-level check; the authoritative check is Orbit
     3. Create LoyaltyTransaction (type=Burn, status=Pending)
        — idempotency key generated here
     4. Call Orbit: POST /orbit/v1/members/{orbitMemberId}/redeem
        Body: { points, reference, idempotencyKey }
     5. On Orbit 200 OK: update LoyaltyTransaction (status=Confirmed)
        Deduct BalanceCache (eventual)
     6. On Orbit timeout/unreachable: status=QueuedForRetry, enqueue retry
        — BalanceCache is NOT deducted yet; the local check is provisional
     7. Return to POS
```

### What happens if Orbit is unreachable

**Degraded mode: queue-and-retry, not silent drop, not double-apply.**

This is the critical design decision and must be documented clearly:

- **Till transactions cannot block on a loyalty engine being down.** If the till had to wait for Orbit, a network glitch would stop the entire checkout. POS availability (R1) trumps loyalty accuracy.
- **Decision: earn/burn requests are queued locally and retried asynchronously.** Vuma stores the `LoyaltyTransaction` with `status=QueuedForRetry` and a retry schedule (exponential backoff, max 24 hours). The customer is served immediately; the loyalty action completes when Orbit is reachable.
- **Idempotency prevents double-application on retry.** Every queued request carries an idempotency key. When Orbit receives a duplicate key, it returns the original response without re-processing (§Idempotency strategy).
- **Balance display during outage.** The till shows the *local* `BalanceCache`. This is explicitly labelled "balance may be updated" on the UI so the cashier and customer know it is a cache, not the authoritative ledger.
- **Retry failure after 24 hours.** The `LoyaltyTransaction` is marked `status=Failed`. An alarm is raised (ops notification). The discrepancy is reconciled by the periodic `ILoyaltyReconciliationService`. The customer is notified (if consent allows) that their loyalty action was delayed.
- **Critical rule:** Vuma **never** silently drops an earn/burn request. The customer must always see confirmation (either immediate from Orbit, or queued with a "pending" indicator). Silent drops create trust failures and reconciliation nightmares.

### Vuma's local cache vs Orbit during offline

When the store has no internet connectivity:
- The till can still sell (POS offline mode).
- Loyalty earn/burn requests are queued locally (SQLite on the till or the store server's outbox).
- `BalanceCache` is the only balance the till can display — it is a local projection, explicitly labelled as such.
- When connectivity is restored, queued requests replay through the sync agent (Stage 04's outbox/inbox pattern), calling Orbit with their idempotency keys.

## Vuma's public API design

This is the **★** item. The public API is Vuma's face to its own front ends (POS terminals, web storefront, Android app) and third-party integrations (loyalty partners, marketplaces). It is **not** Orbit's API — it mirrors Orbit's conventions where sensible to keep the integration thin, but it is a first-class Vuma API with its own auth, versioning, and error contract.

### Authentication / authorization model

| Caller type | Credential | Scoping |
|---|---|---|
| POS terminal | API key (rotating, per-terminal) + terminal certificate | Scoped to `store:{id}` |
| Web storefront | Member token (JWT, short-lived) | Scoped to `customer:{id}` |
| Third-party partner | OAuth 2.0 client credentials | Scoped to registered merchant/store |
| Mobile app | Member token + device certificate | Scoped to `customer:{id}` + `store:{id}` |

- **API keys** for machine-to-machine (till → Vuma API). Rotated monthly; compromised keys revoked via `POST /v1/auth/keys/{id}/revoke`.
- **Member tokens** for customer-facing actions (web, mobile). Signed by Vuma; validated by Vuma's auth middleware. The token carries `sub` (customer UUID), `tenant`, `store`, and `scopes` (`loyalty:read`, `loyalty:earn`, `loyalty:redeem`, `loyalty:admin`).
- **OAuth 2.0 client credentials** for third-party partners. Registered in Stage 30b's control plane. Scopes are merchant-scoped: a partner can only access the merchants they are contracted with.
- **All endpoints** require a valid credential. Unauthenticated requests return `401`. Insufficient scope returns `403`.
- **Tenant isolation** is enforced by a global query filter (Stage 01 pattern) — a tenant cannot access another tenant's loyalty data. A request for another tenant's member returns `404` (not `403` — `docs/API_STANDARDS.md` §4).

### Endpoint list

All endpoints under `/api/v1/loyalty`. Versioned per `docs/API_STANDARDS.md` §2.

#### Members

| Method | Path | Description | Request | Response | Error codes |
|---|---|---|---|---|---|
| `GET` | `/v1/loyalty/members/{id}` | Get a member's profile and current tier | — | `LoyaltyMemberDto` | 401, 403, 404 |
| `POST` | `/v1/loyalty/members/{id}/enroll` | Enroll a customer as a loyalty member | `EnrollMemberRequest` | `LoyaltyMemberDto` | 401, 403, 409, 422 |
| `POST` | `/v1/loyalty/members/{id}/earn` | Earn points (e.g. from a sale) | `EarnRequest` | `EarnResponse` | 401, 403, 422, 429, 503 |
| `POST` | `/v1/loyalty/members/{id}/redeem` | Redeem points (e.g. for a discount) | `RedeemRequest` | `RedeemResponse` | 401, 403, 422, 429, 503 |
| `GET` | `/v1/loyalty/members/{id}/balance` | Get current point balance | — | `BalanceDto` | 401, 403, 404 |
| `GET` | `/v1/loyalty/members/{id}/transactions` | List transaction history | `query: page, pageSize` | `TransactionListDto` | 401, 403, 404 |

#### Tiers

| Method | Path | Description | Request | Response | Error codes |
|---|---|---|---|---|---|
| `GET` | `/v1/loyalty/tiers` | List all tiers (with current member's tier highlighted) | — | `TierListDto` | 401, 403 |
| `GET` | `/v1/loyalty/tiers/{id}` | Get a specific tier's details | — | `TierDto` | 401, 403, 404 |
| `GET` | `/v1/loyalty/members/{id}/tier` | Get a member's current tier and progress to next | — | `MemberTierDto` | 401, 403, 404 |

#### Rewards catalogue

| Method | Path | Description | Request | Response | Error codes |
|---|---|---|---|---|---|
| `GET` | `/v1/loyalty/rewards` | List available rewards | `query: tierId, category` | `RewardListDto` | 401, 403 |
| `GET` | `/v1/loyalty/rewards/{id}` | Get a reward's details | — | `RewardDto` | 401, 403, 404 |

#### Integration / webhook

| Method | Path | Description | Request | Response | Error codes |
|---|---|---|---|---|---|
| `POST` | `/v1/loyalty/webhooks/{id}/notifications` | Orbit-originated webhook (Vuma receives, not sends) | `WebhookNotificationDto` | `202 Accepted` | 401, 403 |

**Note on webhooks:** Vuma **re-emits** Orbit webhooks. When Orbit fires a tier-change or balance-change event, Vuma receives it via `POST /v1/loyalty/webhooks/...`, updates its local cache, and may re-emit its own domain event for Stage 22 (marketing) or Stage 29 (reporting). Vuma never originates loyalty webhooks to external systems — it is a consumer, not a publisher of loyalty events.

### Endpoint-to-Orbit mapping

| Vuma endpoint | Orbit call | Vuma local action |
|---|---|---|
| `POST /members/{id}/earn` | `POST /orbit/v1/members/{orbitMemberId}/earn` | Create `LoyaltyTransaction` (type=Earn, status=Pending); update cache on success |
| `POST /members/{id}/redeem` | `POST /orbit/v1/members/{orbitMemberId}/redeem` | Create `LoyaltyTransaction` (type=Burn, status=Pending); validate cache ≥ requested |
| `GET /members/{id}/balance` | `GET /orbit/v1/members/{orbitMemberId}/balance` | Return `BalanceCache` if < 5 min stale; else call Orbit and update cache |
| `GET /members/{id}/transactions` | (cached) | Query `LoyaltyTransaction` table (Vuma's event log) |
| `GET /tiers` | `GET /orbit/v1/tiers` | Return cached `LoyaltyTier` rows; refresh if > 1 h stale |
| `GET /members/{id}/tier` | `GET /orbit/v1/members/{orbitMemberId}/tier` | Call Orbit; return `MemberTierDto` with cached progress |
| `GET /rewards` | `GET /orbit/v1/rewards` | Return cached `LoyaltyReward` rows |
| `POST /webhooks/...` | (Orbit-initiated) | Update `BalanceCache`, `TierId`, emit domain events |

### Idempotency strategy

**Critical for POS integrations** — duplicate submissions must not double-credit or double-debit, on both Vuma's side and the Orbit call it triggers.

**Mechanism:**
1. Every earn/burn request includes an `Idempotency-Key` header (UUID v7, client-generated).
2. Vuma's API layer checks `LoyaltyTransaction` for an existing row with the same `IdempotencyKey`. If found and `status ∈ {Confirmed, Failed}`, return the cached response immediately (no Orbit call).
3. If found and `status ∈ {Pending, QueuedForRetry}`, return `409 CONFLICT` (or the in-progress response — see §Rate limiting for the choice).
4. If not found, Vuma creates a `LoyaltyTransaction` row with `status=Pending` and the `IdempotencyKey`, then calls Orbit with the same key in the request body (`X-Idempotency-Key` header).
5. Orbit's own idempotency ensures it does not double-process. If Orbit receives a duplicate key, it returns the original response (HTTP 200 with the original result, or HTTP 409 if the key was already processed).
6. Vuma's `LoyaltyTransaction` is updated to `status=Confirmed` with `OrbitTransactionId`.

**Guarantee:** At-least-once delivery to Orbit is guaranteed by Vuma's retry mechanism; at-most-once processing is guaranteed by Orbit's idempotency. Vuma's `IdempotencyKey` + `LoyaltyTransaction` table is the single source of truth for "was this request already handled?"

**Duplicate submission during retry:** If a client retries a request (e.g., network timeout on the client side), the idempotency key ensures the retry returns the original response, not a new earn/burn. The client is responsible for generating a stable idempotency key per logical action.

### Rate limiting and versioning

**Versioning:** URL segment `/api/v1/loyalty/...`. Per `docs/API_STANDARDS.md` §2. Breaking changes bump the version. Additive changes do not.

**Rate limiting** (per `docs/API_STANDARDS.md` §4, `429` status, specified built later for Stage 20/21):
- **Tills:** 100 requests/minute per terminal (API key). Burst allowance of 20.
- **Web/mobile members:** 30 requests/minute per member (member token). Burst allowance of 10.
- **Third-party partners:** Configurable per OAuth client, default 60 requests/minute.
- **Enforcement:** Token bucket. `429 Too Many Requests` with `Retry-After` header. Rate-limit counters are stored in Redis (or an in-memory sliding window for single-instance deployments).
- **Idempotent requests exempt from rate-limit counting** — the `Idempotency-Key` header marks a request as idempotent; the first execution counts against the limit, retries do not. This prevents a legitimate retry from being penalized.

**Burst behavior under rate limit:** A burst that exceeds the limit receives `429` immediately. The till's earn/burn flow handles this by queuing locally (same queue-and-retry as Orbit unreachable — see §Failure-mode tests). The difference: rate-limit queue is a Vuma-side delay; Orbit-unreachable queue is a network delay.

### Webhook / callback support

**Vuma re-emits Orbit webhooks, not its own.**

- Orbit fires events: `tier.changed`, `balance.adjusted`, `reward.fulfilled`.
- Vuma receives them via `POST /v1/loyalty/webhooks/{id}/notifications`.
- Vuma updates its local cache (`BalanceCache`, `TierId`).
- Vuma emits its own domain events (`loyalty.tier.changed`, `loyalty.balance.adjusted`) for downstream modules (Stage 22 marketing, Stage 29 reporting).
- **Vuma does not originate webhooks to third parties.** Third parties poll Vuma's API for changes (they subscribe to a webhook *URL* configured in Stage 30b's control plane, but the webhook is emitted by Vuma's control-plane infrastructure, not by loyalty directly).

**Webhook security:** Orbit webhooks are authenticated via mTLS + a signed payload (HMAC-SHA256 of the body with a shared secret). Vuma validates the signature before processing.

## Non-functional requirements

1. **Consistency guarantees.** The points ledger's real source of truth lives in Orbit. Vuma's `BalanceCache` is **eventually consistent** and **reconcilable** against Orbit — never authoritative. Any operation that depends on an accurate balance (redemption) must validate against Orbit in real time, even if `BalanceCache` suggests sufficient points. The cache is a performance optimization; it is never a correctness guarantee.

2. **Reconciliation.** A `ILoyaltyReconciliationService` runs periodically (hourly) comparing Vuma's `LoyaltyTransaction.RunningBalance` against Orbit's reported balance per member. Discrepancies are logged and alerted. A reconciliation report is emitted as a domain event for Stage 29.

3. **Availability.** Loyalty earn/burn must not block the till. The queue-and-retry degraded mode (§Earn/burn flow) ensures POS availability (R1) is preserved. Loyalty reads (balance display) work from cache even when Orbit is unreachable.

4. **Latency.** Balance lookup: < 50 ms from cache, < 200 ms from Orbit. Earn/burn acknowledgment: < 100 ms (cached write) + async Orbit call. Redemption validation: < 150 ms (cache check + Orbit pre-validation).

5. **Offline tolerance.** Tills queue loyalty actions during network outages. On reconnection, queued actions replay through Stage 04's outbox/inbox with idempotency keys. The till displays "loyalty pending" for any action in `QueuedForRetry` status.

6. **Audit trail.** Every `LoyaltyTransaction` produces a `platform.audit_entries` row. Tier changes produce audit entries. Consent-related loyalty actions produce audit entries via Stage 19. Audit entries are immutable and retained per `docs/SECURITY.md`.

7. **Telemetry.** Daily rollup counters: members enrolled, points earned (total), points burned (total), tier upgrades, redemption failures. Whitelist only — no personal data, no transaction detail (see `docs/TESTING.md` §7).

8. **Read-only enforcement.** A lapsed subscription drops loyalty to read-only: `GET` endpoints work; `POST /earn`, `POST /redeem`, `POST /enroll` return `503` with a neutral error code that does not disclose the billing status (per `docs/TESTING.md` §7 and ADR-028). The `503` response body must not contain the words "subscription", "licence", "lapsed", "billing", or "payment".

9. **POS offline.** The till's local SQLite cache holds `BalanceCache` and queued `LoyaltyTransaction` rows. On reconnection, the sync agent (Stage 04) replays queued actions. No loyalty data is lost during offline periods.

## Testing

### Unit tests (`tests/VumaRetail.UnitTests/Loyalty/`)

1. **Points calculation** (`PointsCalculationTests`)
   - Earn: `EarnRate = 1 point per ZAR`, purchase of R150.00 → 150 points.
   - Earn with rounding edge case: `EarnRate = 0.5 points per ZAR`, purchase of R99.99 → 49.995 → stored at scale 4 as 49.9950, displayed at 2dp as 50.00 (midpoint away from zero, per `Money` type).
   - Earn with tier multiplier: Gold tier × 1.5, R100 purchase → 150 points.
   - Burn: redemption of 200 points from a balance of 500 → 300 remaining.
   - Burn with rounding: redeem 100.5 points → `LoyaltyTransaction.Amount` is `decimal(18,4)`, so 100.5000.
   - Burn more than balance → `InsufficientPointsException`.
   - Points expiry: `PointExpiryDays = 365`, points earned 400 days ago → expired, not counted in balance.
   - Points expiry: points earned 300 days ago → still valid.
   - Negative earn amount → refused.

2. **Tier evaluation** (`TierEvaluationTests`)
   - Member at 900 points with Gold tier threshold at 1000 → Gold tier.
   - Member at 1000 points → Gold tier (boundary inclusive).
   - Member at 1001 points → still Gold (tier doesn't regress on a single purchase; tier thresholds only advance).
   - Tier evaluation calls Orbit (mocked) — Vuma caches the result.
   - Tier progression: Silver → Gold when threshold is crossed.

3. **Points expiry logic** (`PointsExpiryTests`)
   - Points earned today do not expire.
   - Points earned 366 days ago with `PointExpiryDays = 365` → expired.
   - Partial expiry: 500 points earned 400 days ago, 300 points earned 100 days ago → only 500 expired, balance = 300.
   - Expiry does not produce a negative balance.

### Integration tests (`tests/VumaRetail.IntegrationTests/Loyalty/`)

4. **Full earn → Vuma API → vendored Orbit call → balance update → tier re-evaluation** (`FullEarnFlowIntegrationTests`)
   - Create a member (via `POST /v1/loyalty/members/{id}/enroll`).
   - Call `POST /v1/loyalty/members/{id}/earn` with `EarnRequest`.
   - Verify: `LoyaltyTransaction` row created with `status=Confirmed` (or `QueuedForRetry` if Orbit mock is down).
   - Verify: `BalanceCache` updated (or queued).
   - Verify: Orbit mock received the earn call with the correct idempotency key.
   - Verify: tier re-evaluation triggered (if balance crossed a threshold).
   - **Mock the Orbit boundary** via `IOrbitClient` interface — no live Orbit instance needed.

5. **Full redeem → Orbit ledger debit → reward fulfilment** (`FullRedeemFlowIntegrationTests`)
   - Member has 1000 points.
   - Call `POST /v1/loyalty/members/{id}/redeem` for 200 points.
   - Verify: `LoyaltyTransaction` (type=Burn) created.
   - Verify: Orbit mock received debit with correct idempotency key.
   - Verify: reward fulfilment triggered (e.g., a discount code issued).
   - Verify: `BalanceCache` updated to 800.

6. **Idempotency integration** (`IdempotencyIntegrationTests`)
   - Submit the same earn request twice with the same `Idempotency-Key`.
   - Assert: single `LoyaltyTransaction` row, single Orbit call, single point credit.
   - Assert: second request returns the same response as the first.

### Contract/API tests (`tests/VumaRetail.ApiTests/Loyalty/`)

7. **Happy path — earn** (`EarnHappyPathTests`)
   - Authenticated member with valid token → `POST /v1/loyalty/members/{id}/earn` → `200 OK` with `EarnResponse`.
   - Verify response body: points earned, new balance, transaction id.

8. **Happy path — redeem** (`RedeemHappyPathTests`)
   - Member with sufficient points → `POST /v1/loyalty/members/{id}/redeem` → `200 OK` with `RedeemResponse`.

9. **Auth failure** (`AuthFailureTests`)
   - No token → `401 Unauthorized`.
   - Expired token → `401 Unauthorized`.
   - Wrong tenant token → `404 NotFound` (not `403` — see `docs/API_STANDARDS.md` §4).

10. **Malformed input** (`MalformedInputTests`)
    - `EarnRequest` with negative amount → `422 UnprocessableEntity` with `VALIDATION_FAILED`.
    - `RedeemRequest` with missing `IdempotencyKey` → `422`.
    - Invalid UUID in path → `400 BadRequest` with `MALFORMED_REQUEST`.

11. **Idempotency contract** (`IdempotencyContractTests`)
    - Submit earn with key A → 200 OK.
    - Submit earn with key A again → return cached 200 OK (not a new transaction).
    - Submit earn with key B → new transaction, new Orbit call.

12. **Rate-limit behavior under burst** (`RateLimitTests`)
    - 200 requests in 1 second from the same terminal API key.
    - First 100 → `200 OK`.
    - Next 100 → `429 Too Many Requests` with `Retry-After`.
    - Idempotent requests (with `Idempotency-Key`) are exempt from re-counting.

### Concurrency tests (Stage 20 only) (`tests/VumaRetail.IntegrationTests/Loyalty/Concurrency/`)

13. **Two simultaneous burn requests against a balance of 100, each requesting 60** (`SimultaneousBurnTests`)
    - Two `POST /redeem` requests arrive concurrently, each with a unique idempotency key, each requesting 60 points.
    - Balance is 100. Only one can succeed (the ledger can only satisfy one).
    - **Which is correct:** the concurrency control is enforced by Orbit's ledger. Vuma's `BalanceCache` is a local projection and must not be the gate — the gate is Orbit's real-time validation.
    - **Assert:** exactly one of the two burns succeeds; the other returns `422 INSUFFICIENT_POINTS` (after Orbit's real-time validation). No over-redemption occurs.
    - **Implementation note:** Vuma's `POST /redeem` handler calls Orbit's `POST /orbit/v1/members/{id}/redeem` which performs the atomic debit on Orbit's ledger. Vuma does not implement its own concurrency gate — it defers to Orbit. Vuma's local `BalanceCache` is updated asynchronously (eventual consistency).
    - **Test with mocked Orbit:** the mock implements the atomic debit (only one of two concurrent calls succeeds). Vuma's handler must propagate Orbit's response correctly.

### Failure-mode tests (Stage 20 only) (`tests/VumaRetail.IntegrationTests/Loyalty/FailureModes/`)

14. **Orbit unreachable during earn** (`OrbitUnreachableEarnTests`)
    - Mock `IOrbitClient` to throw `TimeoutException` / return `503`.
    - Call `POST /v1/loyalty/members/{id}/earn`.
    - Assert: `LoyaltyTransaction` created with `status=QueuedForRetry`.
    - Assert: `202 Accepted` returned to the till (not a 500 error — the till must not block).
    - Assert: no points credited (the cache is not updated yet).
    - Assert: a retry is scheduled (exponential backoff).

15. **Orbit unreachable during burn** (`OrbitUnreachableBurnTests`)
    - Same as above but for `POST /redeem`.
    - Assert: `LoyaltyTransaction` with `status=QueuedForRetry`.
    - Assert: `BalanceCache` NOT deducted (the local check was provisional).
    - Assert: no double-debit when the retry eventually succeeds (idempotency key protects).

16. **Retry eventually succeeds** (`RetrySuccessTests`)
    - First earn call → Orbit down → `QueuedForRetry`.
    - Retry succeeds after Orbit comes back.
    - Assert: `LoyaltyTransaction.status = Confirmed`, `BalanceCache` updated, Orbit called exactly once.

17. **Retry fails after 24 hours** (`RetryExpiryTests`)
    - Earn call → `QueuedForRetry` → 24 hours pass → retry still fails.
    - Assert: `LoyaltyTransaction.status = Failed`.
    - Assert: alarm raised (ops notification logged).
    - Assert: reconciliation service will detect the discrepancy.

18. **Two simultaneous retries for the same idempotency key** (`DuplicateRetryTests`)
    - Two instances of the retry worker pick up the same queued request.
    - Assert: only one succeeds (the other sees `status=Confirmed` on its first check and skips).
    - This tests the retry worker's own idempotency (distinct from the API-level idempotency key).

### Test scaffolding note

- **Orbit client interface** (`IOrbitClient`): define in `VumaRetail.PublicApi/Abstractions/Loyalty/IOrbitClient.cs`. Mock implementations in tests. This is scaffolding — the real implementation calls Orbit's HTTP API.
- **`LoyaltyTransaction` and `LoyaltyMember` EF configurations**: scaffolding only — the actual schema and migration will be generated when the full stage is implemented.
- **`ILoyaltyReconciliationService`**: scaffolding only — the periodic reconciliation job is defined here; the hosted service implementation follows the Stage 15 pattern (`IHostedService` + Quartz.NET).
- **Stage 19 integration**: tests assume the Stage 19 CRM contract exists (`IConsentService`, `ISegmentService`, `ICustomer360ViewService`). Since Stage 19 is being documented in this same session, the tests will reference these interfaces from the stage-19 doc. If Stage 19's implementation diverges, the Stage 20 tests must be updated accordingly.

## Open questions / hooks left for other stages

- **Stage 21 (Ecommerce, Storefront API & Channels)** depends on Stage 20's member identity. Stage 21's public storefront API will need `GET /v1/loyalty/members/{id}/balance` and `GET /v1/loyalty/members/{id}/tier`. Hook: Stage 21's contract defines how it consumes these endpoints.
- **Stage 22 (Marketing Automation)** needs `IConsentService.IsConsentValidAsync` to gate loyalty marketing messages. Hook: Stage 22's campaign engine references member tier and balance for targeted rewards.
- **Stage 22b (Conversational Commerce)** — the WhatsApp/email assistant may answer loyalty queries (balance, tier, rewards). Per ADR-129, every figure must come from an API result — the assistant calls `GET /v1/loyalty/members/{id}/balance` and `GET /v1/loyalty/members/{id}/tier`, then phrases the answer. It never computes.
- **Stage 29 (Reporting & Admin Dashboard API)** — loyalty KPIs feed the KPI cube. Stage 20's metering counters (members enrolled, points earned/burned) are the source. Hook: Stage 29's cube queries consume the `loyalty` schema's audit trail and the metering rollup.
- **Stage 30b (Vendor Control Plane)** — loyalty usage analytics and subscription billing. Hook: Stage 30b's metering dashboard consumes the same counters Stage 20 produces.
- **Orbit API versioning** — the assumed Orbit API version (`/orbit/v1/...`) is a placeholder. The actual version must be confirmed when Orbit is brought into the repo. Hook: `IOrbitClient` is defined against the assumed version; if Orbit's API differs, the implementation changes but the interface contract does not.
- **Tier thresholds** — cached from Orbit but configured by Vuma's `LoyaltySettings`. Whether tier thresholds are set by Vuma or Orbit (or both) is an open architectural question. Hook: `LoyaltySettings.TierThresholds` is the field to watch.
- **POPIA and loyalty** — loyalty data (points, purchase history) is personal data under POPIA. Consent for `DataProcessing` (Stage 19) gates whether loyalty data may be synced to the cloud or processed by Orbit (which may be hosted outside South Africa). Hook: `IConsentService.GetConsentStateAsync(customerId, ConsentType.DataProcessing)` is the gate; Stage 20 must check it before any cross-border data transfer.

## Notes for later stages

- **Stage 10b (accounts, lay-by, stokvels)** — lay-by payments may earn loyalty points. The earn trigger is in Stage 10b's payment handler, calling Stage 20's earn endpoint.
- **Stage 09b (mixed basket)** — one till selling for two companies may produce loyalty points in two tenants. Each company's loyalty is separate (per-tenant). Hook: the mixed-basket handler calls `POST /v1/loyalty/members/{id}/earn` twice, once per company's member.
- **Stage 07c (cross-company money)** — loyalty rewards redeemed for discounts post to finance. The financial posting rules engine (Stage 07) handles it.
- **Stage 14b (field sales)** — reps may check loyalty balances when selling to members. `GET /v1/loyalty/members/{id}/balance` is the endpoint.
- **Stage 15 (planning)** — loyalty promotion analysis feeds markdown planning. Stage 20's reward redemption data informs Stage 15's sell-through analysis.

---

## Testing summary

| Category | Tests | Status |
|---|---|---|
| Unit — points calculation | 8 | TO BE WRITTEN |
| Unit — tier evaluation | 5 | TO BE WRITTEN |
| Unit — points expiry | 5 | TO BE WRITTEN |
| Integration — full earn/burn flow | 2 | TO BE WRITTEN |
| Integration — idempotency | 1 | TO BE WRITTEN |
| Contract API — happy paths | 2 | TO BE WRITTEN |
| Contract API — auth failure | 1 | TO BE WRITTEN |
| Contract API — malformed input | 1 | TO BE WRITTEN |
| Contract API — idempotency | 1 | TO BE WRITTEN |
| Contract API — rate limit | 1 | TO BE WRITTEN |
| Concurrency — simultaneous burns | 1 | TO BE WRITTEN |
| Failure mode — Orbit unreachable | 4 | TO BE WRITTEN |
| Failure mode — retry expiry | 1 | TO BE WRITTEN |
| Failure mode — duplicate retries | 1 | TO BE WRITTEN |
| **Total** | **35+** | — |

Required coverage: ≥ 80% line on Domain(`loyalty`) + Application(`loyalty`) — see `docs/TESTING.md` §5 and `CLAUDE.md` §8.

**Stubbed/skipped tests:**
- Stage 19 integration tests assume `IConsentService`, `ISegmentService`, `ICustomer360ViewService` exist. Stage 19 doc written in this session; implementation pending.
- Orbit integration tests mock `IOrbitClient`; a live Orbit instance is not available. This is by design.
- Public API contract tests assume `VumaRetail.PublicApi` has the loyalty endpoints registered. The endpoint scaffolding will be generated when the stage is implemented.
- The actual DB schema for `loyalty` module tables does not yet exist (no migration). Integration tests will use in-memory or Testcontainers with scaffolding configurations.
