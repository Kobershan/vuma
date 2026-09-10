# TASK-22B-003 — Document delivery and transport integration

**Status:** IN_PROGRESS · **Stage:** 22b · **Type:** Application / infrastructure / integration

## Objective

Deliver only module-owned documents through Stage 22 transport using short-lived, tenant-scoped,
single-use links and channel policy.

## Current evidence

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
