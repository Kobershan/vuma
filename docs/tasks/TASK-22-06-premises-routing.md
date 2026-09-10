# TASK-22-06 — Shared-premises SKU ownership and routing

**Depends on:** 22-01, 06e, 09b

Add explicit premises_sku_routing setup and collision validation. Bare barcode routes to one company; duplicate ownership requires company-specific variant/barcode.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

