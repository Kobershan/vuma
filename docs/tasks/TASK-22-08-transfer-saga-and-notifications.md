# TASK-22-08 — Transfer saga, idempotent legs and notifications

**Depends on:** 22-07, 06c

Use existing registry saga/outbox. Add intent/leg idempotency, compensation, queue ownership and in-app plus push/email notifications. Keep one company context per handler.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

