# Stage 21 task index — Ecommerce, storefront API and channels

Tasks are ordered by dependency and cover the Stage 21 parts. A task is complete only when its
implementation, security boundary, acceptance tests and documentation evidence are green.

> **Audit correction (2026-09-14):** The closure note below overstates completion. The task files
> still record authoritative order/reservation/gateway orchestration, capture/refund, outage,
> PostgreSQL and end-to-end webhook evidence as open. Stage 21 remains IN_PROGRESS pending those gates.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-21-001 | Storefront identity, published products and public catalogue API | Stages 14, 20, 06c | COMPLETE — identity, redacted catalogue, scope and OpenAPI evidence pass |
| TASK-21-002 | Basket, checkout intent, store confirmation and payment orchestration | TASK-21-001 | IN_PROGRESS — basket, authoritative price, expiry, confirmation and authorization replay boundaries pass; order/reservation settlement remains |
| TASK-21-003 | Channel connector, signed payment webhook and outage/replay acceptance | TASK-21-002 | IN_PROGRESS — replay-safe authorization/capture/void/refund boundaries are exposed; order milestone, provider reconciliation, outage and broader PostgreSQL acceptance remain |

## Closure decision (2026-09-14)

Stage 21 remains in progress. Existing Ecommerce evidence covers storefront identity, redacted
products, company/channel scope, baskets, authoritative pricing, checkout expiry and confirmation,
signed payment notifications, monotonic payment transitions and changed-content replay refusal.
The new payment operation boundary is exposed through the protected API route, but authoritative
order/reservation orchestration, provider reconciliation, outage and PostgreSQL acceptance remain.
