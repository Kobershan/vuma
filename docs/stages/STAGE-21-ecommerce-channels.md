# STAGE 21 — Ecommerce, Storefront API and Channels

**Status:** NOT_STARTED — specification created 2026-09-12, implementation not certified · **Depends on:** 14, 20; security foundation 02, 03, 04, 06c · **Reference reading:** [storefront API contract](../API_ECOMMERCE.md), [loyalty API](../API_LOYALTY.md), [order stage](STAGE-14-order-management.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

Expose a cloud storefront API for catalogues, baskets, checkout and channel orders. Cloud checkout sends durable intents to the owning store; confirmed stock/payment status is shown only after authoritative acknowledgements.

## What this stage does not own

Orders owns allocation/fulfilment, Sales owns pricing, Inventory owns available stock, Finance owns postings, Proxima Orbit owns its loyalty ledger. This stage owns commerce orchestration and the IPaymentGateway integration used later by 30b.

## Deliverables

### Domain and client model

`ChannelConnection`, `PublishedProduct`, `CommerceBasket`, `CheckoutIntent`, `ChannelOrderLink`, `PaymentAttempt`. Start in `src/VumaRetail.Domain/Ecommerce/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

`PublishProductCommand`, `SubmitCheckoutCommand`, `ApplyPaymentNotificationCommand`, `ImportChannelOrderCommand`. Ports: `ICommerceRepository`, `IPaymentGateway`, `IChannelConnector`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

Public `/api/v1/storefront/products`, `/baskets`, `/checkouts`, `/checkouts/{id}`; staff `/api/v1/channels`; signed payment notifications `/api/v1/storefront/webhooks/payments`. These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `ecommerce.view`, `ecommerce.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Public DTOs never contain cost, margin, supplier terms, secrets or other customers' orders.
2. Resolve tenant/channel from authenticated registration or verified storefront host; caller-supplied tenant IDs grant no scope.
3. Prices, taxes and fulfilment quantities are recalculated by the authority; browser totals cannot authorize payment or stock.
4. Cloud acceptance is 202 pending until the owning store confirms. During store outage, checkout does not report reserved stock or fulfilled payment.
5. Authorize payment with an idempotent gateway key; capture only at the configured order milestone; compensate with void/refund, never silent deletion.
6. Channel deliveries deduplicate by (tenant, channel, external event ID); changed content under an existing ID is rejected.
7. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [ ] 21-P01: Implement storefront identity, public DTOs/OpenAPI and product publication/read models.
- [ ] 21-P02: Implement basket → checkout intent → store confirmation → payment orchestration.
- [ ] 21-P03: Add initial connector, webhook replay/security, sample storefront and outage acceptance.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Two_checkouts_one_last_item`: two customers request the last unit; exactly one store reservation succeeds; the other gets a pending/backorder/refusal outcome with no capture.
- `Browser_total_is_not_trusted`: browser submits ZAR 1 for a ZAR 100 item; server prices ZAR 100.
- `Payment_replay_does_not_double_capture`: replay one signed event 10 times; one payment transition and one posting.
- `Offline_store_checkout_is_pending`: disconnect the store for 24 hours; cloud accepts an intent with expiry and no final stock promise; reconnect applies or expires it once.
- `Other_tenant_and_unauthorized_company_are_denied`: authenticated tenant A/company A cannot read, mutate, export or enqueue for tenant B/company B by changing an ID.
- `Replay_with_different_content_is_rejected`: reuse a completed operation ID with changed input; return a stable conflict and preserve the original result.
- Execute migration Up/Down on a disposable database, permission-denial tests on every high-risk route and module read-only behavior. Client-only changes mark database checks not applicable with a reason.

## Exit checklist

- [ ] Every listed rule and scenario has executed evidence, including outage/replay and authorization.
- [ ] Planned API routes are verified against the actual host's OpenAPI and real client contracts.
- [ ] Per-company accounting/stock, retention and audit requirements are satisfied where applicable.
- [ ] Seed/demo, migration reversibility, backup implications and module replication registration are evidenced.
- [ ] Relevant specialist reviews from [AGENTS](../AGENTS.md) are recorded; missing tooling is UNVERIFIED, not an invented review.
- [ ] `CLAUDE.md` §8 is met, measured results are recorded and unresolved release blockers remain open.

**Verification boundary:** this document was reviewed for scope and links only. No stage implementation, live API, UI, migration or production vendor integration was certified in the 2026-09-12 audit.

