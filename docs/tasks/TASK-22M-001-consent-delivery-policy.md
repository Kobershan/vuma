# TASK-22M-001 — Consent-aware marketing delivery policy

Evaluates CRM consent immediately before marketing delivery, while allowing explicitly classified
transactional messages through. Email, SMS and push map to their dedicated CRM purposes; WhatsApp
marketing fails closed until the consent taxonomy has an explicit purpose for that channel.

Verification: `MarketingDeliveryPolicyTests` passes 3/3 on 2026-09-13. Campaign persistence, queue
idempotency, quiet hours, provider transports, signed callbacks and operator APIs remain open.
