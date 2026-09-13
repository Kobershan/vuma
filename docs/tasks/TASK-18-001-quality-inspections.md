# TASK-18-001 — Inspection plans and immutable evidence

**Status:** IN_PROGRESS · **Stage:** 18 · **Type:** Domain, application, persistence, API, tests

## Objective

Version inspection plans and record immutable inspection results with scoped evidence and replay
protection.

## Acceptance criteria

- Only published inspection plans can be used for results, and each result retains its plan version.
- A repeated operation with identical content is idempotent; changed content is rejected.
- Tenant/company authorization and module entitlement are enforced on every write route.
- Migration Up/Down and OpenAPI route evidence are recorded against PostgreSQL.

## Evidence

- Quality unit suite: **10/10 passed**.
- Quality API/permission suite: **3/3 passed**.
- Stage 18 migration Up/Down: **1/1 passed** (`ManufacturingMigrationTests.Stage18_quality_migrations_up_and_down_are_reversible`).
- Quality creation routes now bind the request `CompanyId` into the ambient company scope before
  dispatching the command; the authorized inspection-plan API test proves the route returns `201`
  instead of failing its company-context guard.

## Remaining

End-to-end inspection against a real held stock lot, evidence attachment retention, changed-payload
replay, seed/backup impact and specialist review remain open.
