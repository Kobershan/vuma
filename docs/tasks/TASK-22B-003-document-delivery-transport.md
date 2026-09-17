# TASK-22B-003 — Document delivery and transport integration

**Status:** COMPLETE · **Stage:** 22b · **Type:** Application / infrastructure / integration

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
- The conversation module registers the Stage 22 sender boundary (`IWhatsAppSender`), sends the
  generated one-time link in the outbound conversation reply, and normalizes email inbound messages
  through the same state-machine path. Conversation tests pass **32/32**.
- 2026-09-17: PostgreSQL HTTP coverage proves `/conversations/inbound/email` normalizes a bound,
  consented email into the same tenant-scoped conversation/transcript store (**1/1**).
- 2026-09-17: Outbound WhatsApp transport attempts now persist sent/failed audit rows with bounded
  failure reasons through `IConversationDeliveryAudit`; migration
  `20260917044508_Stage22bConversationDeliveryAudit` passes PostgreSQL up/down/up execution.
  Conversation API coverage passes **5/5**.

## Remaining work

- None. Delivery attempts are durable and observable; sensitive document handlers retain fresh
  verification and persisted account/company scope checks.
- Prove every sensitive document path uses fresh verification and account/company scope.

## Definition of done

All outbound messages use the owning module’s document reference and Stage 22 transport policy;
delivery failures are durable and observable.
