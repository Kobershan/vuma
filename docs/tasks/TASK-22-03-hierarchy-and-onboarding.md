# TASK-22-03 — Multi-location hierarchy and onboarding wizard

**Depends on:** 22-01, 22-02

Implement group_hierarchy_node with store/regional/head_office, owned/franchised, parent, code and stock_holding. Validate one parent, no cycles, prefix and relinks.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

