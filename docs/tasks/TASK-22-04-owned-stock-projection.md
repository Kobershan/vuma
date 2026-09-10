# TASK-22-04 — Cost-free owned-store stock visibility projection

**Depends on:** 22-03, 08c

Build a dedicated stock-on-hand-only projection across owned nodes. Structurally exclude franchised nodes. Add a reflection guard forbidding cost, margin and GL fields.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

