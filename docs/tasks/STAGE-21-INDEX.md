# Stage 21 task index — Ecommerce, storefront API and channels

Tasks are ordered by dependency and cover the Stage 21 parts. A task is complete only when its
implementation, security boundary, acceptance tests and documentation evidence are green.

| ID | Scope | Dependencies | Status |
|---|---|---|---|
| TASK-21-001 | Storefront identity, published products and public catalogue API | Stages 14, 20, 06c | IN_PROGRESS — implementation and OpenAPI slice green; full isolation/DTO acceptance remains |
| TASK-21-002 | Basket, checkout intent, store confirmation and payment orchestration | TASK-21-001 | IN_PROGRESS — basket foundation added; checkout/payment remains |
| TASK-21-003 | Channel connector, signed payment webhook and outage/replay acceptance | TASK-21-002 | NOT_STARTED |
