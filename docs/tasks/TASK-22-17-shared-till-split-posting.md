# TASK-22-17 — Shared-premises basket routing and split posting

**Depends on:** 22-06, 22-16, 09b

Resolve ownership from explicit routing and commit company price, stock and GL locally through saga legs. Receiving routes by supplier and PO.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

