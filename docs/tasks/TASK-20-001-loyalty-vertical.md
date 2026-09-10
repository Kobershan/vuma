# Task

## Status

COMPLETE

## Stage

Stage 20 — Loyalty programme & Public API

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE, API, TESTING

## Objective

Build the full Stage 20 vertical: `loyalty` schema (member/transaction/tier/reward/
settings), earn/burn through `IOrbitClient` (default `InMemoryOrbitClient` fake +
`HttpOrbitClient` stub; `vendor/ProximaOrbit` deferred — no upstream), idempotent
earn/burn with queue-and-retry (till never blocks), consent-gated marketing actions,
reconciliation service + retry hosted service, loyalty permissions + manifest, migration,
and the **PublicApi** host endpoints with PublicApi-owned DTOs, rate limiting (429 +
Retry-After) and neutral-503 read-only writes.

## Why

Stage doc exists (`docs/stages/STAGE-20-loyalty-public-api.md`) and the complete vertical is
implemented, including the Orbit boundary, local queue/retry path, public API, migration, and seed.

## Scope

- Domain (`src/VumaRetail.Domain/Loyalty/`, one type per file):
  - Keep compat: `LoyaltyMember`, `LoyaltyTransaction` (+`IdempotencyKey`), statuses,
    `InsufficientPointsException`, `InvalidPointsException`.
  - New: `LoyaltyTier` (cached tier row), `LoyaltyReward` (catalogue row),
    `LoyaltySettings` (earn-rate/expiry config), `LoyaltyCalculator` (pure: earn points =
    amount × rate × tier multiplier, scale-4 store / 2dp display, expiry evaluation),
    `LoyaltyEnums.cs`, `LoyaltyExceptions.cs` (LOYALTY_* codes).
  - Consent stays in Crm (moved by TASK-19-001).
- Application (`src/VumaRetail.Application/Loyalty/`):
  - Ports `LoyaltyPorts.cs`: 5 repositories + `IOrbitClient` (Earn/Redeem/Balance/Tier
    calls with idempotency key) + `ILoyaltyReconciliationService`.
  - Commands (Write): EnrollMember, EarnPoints (idempotent on key; consent-gated where
    marketing; creates Pending tx → Orbit → Confirmed, or QueuedForRetry on Orbit
    outage), RedeemPoints (cache pre-check + Orbit authoritative debit; concurrent burns
    serialize on Orbit), RecordTierCache, RecordRewardCache, RetryQueuedTransactions.
    Queries: GetBalance (cache <5min else Orbit), ListTransactions, GetTier, ListTiers,
    ListRewards.
  - `LoyaltyPermissions` (`loyalty.member.*`, `loyalty.earn`, `loyalty.redeem`,
    `loyalty.tier.view`, `loyalty.reward.view`, `loyalty.admin`), `LoyaltyModuleManifest`
    (flag `loyalty`).
  - Retry + reconciliation hosted services (Quartz-style `IHostedService`, Stage-15
    pattern); 24h retry expiry → Failed + ops alarm event.
- Infrastructure: `Schemas.Loyalty`, EF configs (idempotency partial-unique
  `ux_loyalty_transactions_idempotency`, balance numeric(18,4)), repos,
  `InMemoryOrbitClient` (thread-safe atomic ledger — the concurrency gate in tests and
  the default registration) + `HttpOrbitClient` (config-gated; real credentials
  deferred), `AddVumaLoyalty()`, DbSets.
- PublicApi (`src/VumaRetail.PublicApi/` — own DTOs, NEVER Contracts/Domain entities):
  member enroll/earn/redeem/balance/transactions, tiers, rewards, Orbit webhook receiver
  (mTLS+HMAC documented; signature check enforced when secret configured, logged-skip in
  dev), fixed-window rate limiting (tills 100/min, members 30/min, partners 60/min;
  idempotent retries exempt), neutral 503 on writes while read-only (no billing words).
- Database: one reversible migration (`Stage20_Loyalty`); Down tested.

## Out of Scope

`vendor/ProximaOrbit` subtree (no upstream to merge — deferred with fake satisfying the
port); OAuth/API-key auth scheme (Stage 30b; v1 uses JWT + permissions like StoreServer);
GL posting of redemptions (hook documented for 07/10); `docs/API_LOYALTY.md` is written in
TASK-20-002.

## Architecture

Same handler rules as Stage 19. Orbit is the concurrency gate for burns (never the local
cache). BalanceCache explicitly stale-labelled (`BalanceCacheAsAt`). No finance refs. No
cross-schema FKs. PublicApi → Application + Infrastructure only (arch-test clean).

## Dependencies

Stage 10 (promotion extension point — consumed, not replaced), Stage 19 contracts
(`IConsentService`, `ISegmentService`, 360 view), 04b (entitlement + read-only).

## Relevant Files

`docs/stages/STAGE-20-loyalty-public-api.md`, `src/VumaRetail.PublicApi/Program.cs`
(stub), `src/VumaRetail.Application/Planning/Hosting/PlanningHostedServices.cs`
(pattern), `src/VumaRetail.Infrastructure/DependencyInjection/
PlanningServiceCollectionExtensions.cs` (pattern).

## Relevant Documentation

Stage-20 doc (all), API_STANDARDS.md §4/§9, TESTING.md §7 (neutral 503, metering
whitelist), ADR-151.

## Implementation Requirements

- Earn/burn NEVER block the till: Orbit outage → QueuedForRetry + 202 Accepted.
- Idempotency: same key + same body → original response; same key + different body → 409.
- Burn pre-check on cache is provisional; Orbit debit is authoritative.
- Webhook receiver updates cache + emits domain events for 22/29.

## Data/Database Impact

New schema `loyalty`, five tables. No changes to existing tables.

## API Impact

New PublicApi surface `/api/v1/loyalty/*` (versioned, OpenAPI). Additive only.

## Security

Member-token scoping (customer can only touch own id unless staff permission); webhook
signature; tenant isolation (other tenant → 404); rate limits.

## Multi-Company/Tenant Impact

Loyalty is per-company (company_id required); mixed-basket (09b) calls earn once per
company — documented hook.

## Sync/Offline Impact

`loyalty` rows `[Replicated]` + registry rows; till offline queue replays with
idempotency keys through the outbox pattern.

## Acceptance Criteria

- Earn → Orbit → Confirmed + cache; Orbit down → Queued + 202; retry → Confirmed once.
- Double-submit same key → one tx, one Orbit call. Same key different body → 409.
- Two concurrent 60-point burns on 100 balance → exactly one succeeds.
- Consent-withdrawn member → marketing earn gated 422 CONSENT_NOT_GIVEN.
- Read-only: GETs 200, POSTs neutral 503 (no billing words).
- Burst 200/min from one key → 100×200 then 429s with Retry-After.

## Tests Required

- Unit: calculator (rounding 49.9950/50.00, multipliers, expiry), tier eval, idempotency
  matrix, rate-limiter, reconciliation diff.
- Integration (real PG): full earn/redeem flows vs InMemoryOrbit, idempotent replay,
  concurrent burns, outage→queue→retry→confirmed, retry-expiry→Failed, webhook cache
  update.
- PublicApi contract tests: happy/401/404/422/409/429/503-neutral.

## Edge Cases

- Negative/zero earn → 422; redeem > Orbit balance → 422 (cache said OK).
- Retry worker double-pickup → single confirm (status-check-then-claim).
- Stale cache (>5min) → live Orbit read-through.

## Definition of Done

CLAUDE.md §8 (same as 19-001) + `docs/API_LOYALTY.md` written; committed + pushed.

## Follow-up Findings

- Real Orbit credentials/endpoint (deferred — needs vendor account).
- OAuth client-credentials + member-token issuance (Stage 30b).
- Tier-threshold ownership (Vuma vs Orbit) open question per stage doc.

## Work Log

- 2026-09-10: Loyalty domain, application, infrastructure, PublicApi contracts/endpoints, migration, permissions, manifest, retry/reconciliation, and seed verified on main.
- 2026-09-10: Unit suite 52/52 and real PostgreSQL loyalty integration suite 12/12 passed; public API suite 7/7 passed.

- 2026-09-10: task written; implementation starts.
