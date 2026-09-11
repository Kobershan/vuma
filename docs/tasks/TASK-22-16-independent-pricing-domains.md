# TASK-22-16 — Group, franchise and shared-premises pricing

**Depends on:** 22-05, 22-06, 22-14

Implement owned-store retail base and local overrides, franchise wholesale fallback and independent shared-premises lists. Transfer cost can never be a customer price.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

## Current evidence

Registry persistence and permission-gated API routes now support independent owned-store group
prices, flat franchise wholesale prices with franchisee overrides, and company-owned shared-premises
retail prices. Each model stores currency and exposes only its own effective customer price; transfer
cost fields are not referenced. Ownership, franchise relationship, and active premises occupancy are
validated before writes, with unique tenant-scoped keys and reversible PostgreSQL migration coverage.

## Remaining work

The existing company sales-price resolver and POS split-posting path still need to consume these
registry prices during a real basket, rather than treating the registry API as the final price source.
