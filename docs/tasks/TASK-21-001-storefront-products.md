# TASK-21-001 — Storefront identity, published products and catalogue API

**Status:** IN_PROGRESS · **Stage:** 21 · **Type:** Domain / application / infrastructure / API / test

## Objective

Implement the first Stage 21 vertical slice: a registered storefront/channel identity and a public,
tenant-safe published product read model containing sell-facing content, price and availability
as-of data, while structurally excluding cost, margin, supplier terms and secrets.

## Why

Stage 21 is currently unimplemented. This task establishes the authority and DTO boundary required
before baskets and checkouts can safely reference a storefront.

## Scope

- Add `ChannelConnection` and `PublishedProduct` under `src/VumaRetail.Domain/Ecommerce/`.
- Add scoped repositories, publication/query handlers and granular Ecommerce permissions.
- Add the `ecommerce` persistence schema, EF mappings, migration and seed/read fixtures.
- Add public storefront DTOs/routes under the appropriate public host and staff channel-management
  routes under `src/VumaRetail.Web/`.
- Verify host OpenAPI, tenant/channel scope, public DTO redaction and read-only behavior.

## Out of Scope

Basket checkout, payment capture, external connectors and webhook processing; those belong to
TASK-21-002 and TASK-21-003.

## Architecture and boundaries

Storefront identity is derived from the authenticated channel registration or verified host; no
caller-supplied tenant ID is authoritative. Ecommerce owns its read model and references Catalog,
Sales and Inventory through application ports only. No cross-schema foreign keys or public exposure
of internal domain entities are permitted.

## Dependencies

Stages 06c, 10, 14 and 20; `docs/API_ECOMMERCE.md`; `docs/stages/STAGE-21-ecommerce-channels.md`;
`docs/stages/STAGE-SHARED-REQUIREMENTS.md` §§1–5.

## Acceptance criteria

- A registered channel can publish and read a versioned product projection.
- Public catalogue results contain only published sell-facing fields, sell price/currency and
  availability `AsAt`; cost, margin, supplier terms, secrets and tenant internals are absent.
- A caller cannot select another tenant or channel by changing an ID or query parameter.
- Staff channel writes require `ecommerce.manage`; catalogue reads require the appropriate view
  permission or registered public channel identity.
- Migration Up/Down, OpenAPI and real PostgreSQL tenant-isolation evidence pass.

## Tests required

Unit publication/version and redaction tests; PostgreSQL migration Up/Down; public API contract
tests for 200/401/403/404; OpenAPI route checks; cross-tenant/channel isolation tests.

## Definition of done

Implementation, tests, migration evidence, API contract documentation and work log are green and
committed/pushed. Remaining checkout/payment work is recorded in TASK-21-002.

## Work log

- 2026-09-13: task decomposed from Stage 21-P01; channel identity, published-product persistence/read/publication routes and OpenAPI evidence implemented. Basket foundation begins in TASK-21-002.
