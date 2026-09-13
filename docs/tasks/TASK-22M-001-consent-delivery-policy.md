# TASK-22M-001 — Consent-aware marketing delivery policy

Evaluates CRM consent immediately before marketing delivery, while allowing explicitly classified
transactional messages through. Email, SMS and push map to their dedicated CRM purposes; WhatsApp
marketing fails closed until the consent taxonomy has an explicit purpose for that channel.

Verification: `MarketingDeliveryPolicyTests` passes 6/6 on 2026-09-13, including recipient-timezone
quiet-hours scheduling. Campaign persistence, queue
idempotency, provider transports, signed callbacks and operator APIs remain open. The quiet-hours
scheduler is now covered by three additional cases in the same suite (6/6 total).
