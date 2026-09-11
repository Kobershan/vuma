# TASK-22B-003 — Document delivery and transport integration

**Status:** IN_PROGRESS · **Stage:** 22b · **Type:** Application / infrastructure / integration

## Objective

Deliver only module-owned documents through Stage 22 transport using short-lived, tenant-scoped,
single-use links and channel policy.

## Current evidence

- Delivery tokens are one-time, expiring, tenant-scoped and durably consumed; invoice references are
  resolved against the owning sales repository before delivery. WhatsApp signatures fail closed.
- The transport sender/template/email integration is not present and remains a dependency on Stage 22.

- `DocumentDeliveryToken` and `DocumentDeliveryService` enforce verification freshness and granted
  consent, expire after 24 hours, support revocation, and audit fetches.
- WhatsApp webhook signature verification and normalized inbound handling exist.

## Remaining work

- Connect outbound delivery to Stage 22’s sender/template interfaces.
- Add transport delivery/failure/retry audit records and email inbound normalization.
- Prove every sensitive document path uses fresh verification and account/company scope.

## Definition of done

All outbound messages use the owning module’s document reference and Stage 22 transport policy;
delivery failures are durable and observable.
