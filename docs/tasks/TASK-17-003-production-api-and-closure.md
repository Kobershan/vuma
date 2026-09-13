# TASK-17-003 — Manufacturing API, replay, genealogy and closure evidence

**Status:** IN_PROGRESS · **Stage:** 17 · **Type:** API, sync/offline, security, verification

## Objective

Expose production commands and genealogy/capacity queries, prove offline replay convergence and close
the stage with authorization, migration, seed, backup and specialist evidence.

## Dependencies

TASK-17-002.

## Acceptance criteria

- Planned production routes match the actual OpenAPI contract and have examples/error responses.
- Unauthorized tenant/company and module-entitlement access is denied on every route.
- Offline production operations replay exactly once, including changed-payload rejection.
- Capacity, genealogy, backup implications and seed evidence are documented.
- Relevant specialist reviews and all measured test counts are recorded before stage closure.

## Work log

- 2026-09-12: canonicalized from Stage 17 part 17-P03. Implementation not yet started.
- 2026-09-13: added secured production-order create/release/issue/receipt/scrap/close routes under
  `/api/v1/manufacturing/production-orders`, with manufacturing module entitlement and manage
  permission enforcement. OpenAPI examples, genealogy/capacity queries, offline replay evidence,
  backup/seed evidence and final specialist verification remain open.
- 2026-09-13: added OpenAPI path coverage for all six production execution routes. The integration
  suite must still execute against a created production order once stock/financial boundaries are
  wired; current focused domain/API build evidence is not stage-closure evidence.
- 2026-09-13: added tenant-scoped production execution/genealogy read and routing-capacity query
  surfaces at `GET /api/v1/manufacturing/production-orders/{id}` and `/capacity`, including the
  release-time routing/material snapshot and all idempotent issue, receipt and scrap records. Web
  build passes with 0 errors; end-to-end PostgreSQL execution and specialist closure review remain.
- 2026-09-13: added an integration authorization assertion proving a view-only user cannot create
  production orders. Every production command route remains protected by the manufacturing module
  entitlement and manage permission; end-to-end execution and closure evidence remain open.
- 2026-09-13: CI run `34746409342` passed all required gates, including migration, security scan,
  API tests and client packaging. The stage is still open pending real PostgreSQL execution,
  backup/seed evidence and specialist review.
- 2026-09-13: CI run `34743485288` passed the full required workflow, including the production API
  build and OpenAPI test. End-to-end authorized execution, capacity/genealogy queries, backup/seed
  evidence and specialist closure review remain open.
