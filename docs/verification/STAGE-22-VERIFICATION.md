# Stage 22 verification evidence

Date: 2026-09-18

Repository-owned marketing automation is complete:

- consent-aware campaign and outbound-message policy is evaluated immediately before delivery;
- campaign, journey, enrollment, attribution, delivery-result and retry state is tenant/company scoped;
- operator APIs expose guarded campaign, message, journey, attribution, delivery and suppression paths;
- signed provider callbacks are replay-safe and reject changed payloads;
- the configured HTTPS transport carries durable message identity and provider idempotency metadata;
- bounded due-queue execution and the hosted delivery sweep preserve queued retry state on failure;
- migration up/down/up and focused PostgreSQL/API acceptance are recorded in the stage and task logs.

Evidence:

- focused marketing/conversation unit tests: **67 passed, 0 failed**;
- focused marketing transport test covers queued row → HTTPS provider response → durable Sent state;
- existing marketing migration, API, retry and hosted-worker evidence remains recorded in the task files;
- architecture and repository regression evidence is recorded in the current verification handoff.

The live provider account, endpoint secret and provider-specific callback deployment are deployment
configuration, not repository state. No live vendor result is claimed without those credentials.
