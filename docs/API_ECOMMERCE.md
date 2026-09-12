# API_ECOMMERCE — planned storefront and channel contract

Status: PROPOSED, 2026-09-12. These routes are Stage 21 deliverables, not a deployed API.
See [Stage 21](stages/STAGE-21-ecommerce-channels.md), [API standards](API_STANDARDS.md)
and [API_LOYALTY](API_LOYALTY.md). This fills a referenced but missing contract document.

## Authentication and scope

Public catalogue access is scoped to a published storefront host/channel registration. Customer
tokens address only their own baskets/orders; server integrations use registered tenant/channel
credentials. Never accept a caller's tenant ID as authority. Public DTOs contain no internal cost,
margin, supplier terms or other customers' data. Staff administration uses separate permissions.

## Planned endpoints

| Method/path | Contract |
|---|---|
| GET `/api/v1/storefront/products` | Published content, sell price/currency, availability AsAt; page size 50, maximum 200 |
| POST `/api/v1/storefront/baskets` | Creates an owned basket; client prices are advisory only |
| POST `/api/v1/storefront/checkouts` | Requires Idempotency-Key; returns 202, operation ID, expiry and status URL |
| GET `/api/v1/storefront/checkouts/{id}` | Owner-only status: pending, confirmed, rejected, expired, compensation-pending |
| POST `/api/v1/storefront/webhooks/payments` | Signed provider callback, durable event deduplication, registration-derived scope |
| GET/POST `/api/v1/channels` | Permissioned channel configuration; never returns stored credentials |

## Execution and errors

The owning store/company authorizes stock, price, credit, tax and order acceptance. Cloud stores a
durable intent and routes it to that authority; cloud receipt alone cannot promise stock or complete
a payment. Default checkout intent expiry is 24 hours; revalidate stale prices and consent before
confirmation. Never automatically fail over writes to a second authority during a partition.

Same idempotency key plus same content returns the original status/result; changed content is 409.
Use 401/403 for credential/permission failures, non-disclosing 404 for another customer's resource,
422 for business refusal and 429 plus Retry-After for rate limits. Provider failures become explicit
pending/unknown outcomes; capture, void and refund use stable operation keys and reconciliation.

## Required verification

Prove one last item cannot be sold twice, client price tampering is rejected, delayed payment
callbacks do not double-capture, a customer cannot inspect another customer's checkout, and a
24-hour offline store never creates a false confirmed result. Run against the actual CloudApi/public
host composition before releasing mobile or storefront clients.
