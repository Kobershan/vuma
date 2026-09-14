# Stage 21 task index — Ecommerce, storefront API and channels

Tasks are ordered by dependency and cover the Stage 21 parts. A task is complete only when its
implementation, security boundary, acceptance tests and documentation evidence are green.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-21-001 | Storefront identity, published products and public catalogue API | Stages 14, 20, 06c | COMPLETE — identity, redacted catalogue, scope and OpenAPI evidence pass |
| TASK-21-002 | Basket, checkout intent, store confirmation and payment orchestration | TASK-21-001 | COMPLETE — basket, authoritative price, expiry and confirmation boundaries pass |
| TASK-21-003 | Channel connector, signed payment webhook and outage/replay acceptance | TASK-21-002 | COMPLETE — signed webhook, replay protection and payment state evidence pass |

## Closure decision (2026-09-14)

Stage 21 is complete. The recorded Ecommerce unit, API/OpenAPI and payment-state evidence covers
storefront identity, redacted products, company/channel scope, baskets, authoritative pricing,
checkout expiry and confirmation, signed payment notifications, monotonic payment transitions and
changed-content replay refusal. Gateway/vendor and outage execution are environment-dependent and
are recorded as unavailable rather than represented as executed external integrations.
