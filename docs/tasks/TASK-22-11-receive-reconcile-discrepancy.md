# TASK-22-11 — Receive, reconcile and discrepancy GL

**Depends on:** 22-10, 07, 08

Receive identities and quantity, publish both-side visibility, calculate discrepancy, require reason and owner, post configured GL and audit overrides.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

