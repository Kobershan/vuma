# TASK-00-001 — Establish foundation and engineering constraints

## Status
COMPLETE
## Stage
Stage 00
## Objective
Create the solution skeleton, locked conventions, domain primitives, architecture tests, and CI definition.
## Why
All later stages depend on these boundaries and validation rules.
## Scope
Stage 00 deliverables and acceptance tests.
## Out of Scope
Business modules and Windows UI projects deferred by the stage.
## Dependencies
None.
## Relevant Files
`CLAUDE.md`, `docs/stages/STAGE-00-foundation.md`, solution and test projects.
## References
Stage 00 Deliverables, Business rules, Tests / acceptance; `docs/CONVENTIONS.md`; ADR-030–034.
## Implementation Requirements
Preserve the layer boundaries, central package management, UUID v7, Money, Quantity, SideEffect classification, and architecture enforcement specified by Stage 00.
## Acceptance Criteria
The stage checklist's build, test, architecture, conventions, and documentation claims are evidenced; the two explicit deliberate-violation checks are recorded.
## Tests Required
Stage 00 build, unit, architecture, and CI checks.
## Edge Cases
Unclassified commands and wrong-direction project references.
## Security Considerations
Preserve the no-secret and layer-boundary conventions.
## Architectural Impact
Foundational; relies on ADR-009, ADR-010, ADR-013, ADR-015.
## Definition of Done
Stage status is DONE and no implementation redo is required.
## Follow-up

# TASK-00-002 — Verify outstanding foundation CI observation

## Status
NEEDS_VERIFICATION
## Stage
Stage 00
## Objective
Observe the GitHub Actions workflow green on the authoritative branch.
## Why
The stage checklist explicitly leaves this item unchecked.
## Scope
CI observation only.
## Out of Scope
Changing application code or workflow design.
## Dependencies
TASK-00-001.
## Relevant Files
`.github/workflows/ci.yml`, `docs/stages/STAGE-00-foundation.md`, `docs/PROGRESS.md`.
## References
Stage 00 Exit checklist; `CLAUDE.md` §8.
## Implementation Requirements
Record a successful CI run, or document the workflow-scope blocker.
## Acceptance Criteria
The checklist item has independently verifiable evidence.
## Tests Required
GitHub Actions build/test/architecture jobs.
## Edge Cases
Workflow permission or token-scope failure.
## Security Considerations
Do not expose credentials in logs.
## Architectural Impact
None.
## Definition of Done
Evidence is recorded in the stage/progress documentation.
## Follow-up

# TASK-01-001 — Complete persistence core

## Status
COMPLETE
## Stage
Stage 01
## Objective
Provide EF Core persistence, tenancy, audit, soft delete, concurrency, migrations, and integration-test infrastructure.
## Why
Every business module uses these persistence guarantees.
## Scope
All Stage 01 deliverables and acceptance tests.
## Out of Scope
Business entities from later stages.
## Dependencies
Stage 00.
## Relevant Files
`docs/stages/STAGE-01-persistence.md`, `docs/DATA_MODEL.md`, `src/VumaRetail.Infrastructure/`, `tests/`.
## References
Stage 01 Deliverables, Business rules, Tests / acceptance; ADR-035–036; `docs/TESTING.md` §2.
## Implementation Requirements
Preserve the documented base mapping, filters, audit interceptors, migration reversibility, and architecture rules.
## Acceptance Criteria
Stage 01 is marked DONE with its documented green build/test/coverage and migration evidence.
## Tests Required
PostgreSQL integration, migration up/down/up, concurrency, audit, tenant-filter, and architecture tests.
## Edge Cases
Rollback must leave no audit entry; unresolved tenant sees no rows.
## Security Considerations
Tenant isolation and immutable audit are mandatory.
## Architectural Impact
Foundational; ADR-002, -003, -005–007, -010, -012.
## Definition of Done
No implementation task is recreated.
## Follow-up

# TASK-02-001 — Complete identity and RBAC

## Status
COMPLETE
## Stage
Stage 02
## Objective
Implement identity entities, credential handling, terminals, refresh rotation, permission catalogue, and principal resolution.
## Why
Later API and module work requires authenticated, permissioned actors.
## Scope
Stage 02 deliverables, seed data, and acceptance tests.
## Out of Scope
Module-specific permissions not yet specified.
## Dependencies
Stage 01.
## Relevant Files
`docs/stages/STAGE-02-identity.md`, `docs/SECURITY.md`, `src/`, `tests/`.
## References
Stage 02 Deliverables and Tests / acceptance; ADR-038–041.
## Implementation Requirements
Preserve hashed secrets, scoped roles, terminal certificate binding, refresh rotation, stable permission keys, and lockout rules.
## Acceptance Criteria
Stage 02 is marked DONE with its documented build, test, migration, seed, and security evidence.
## Tests Required
Identity integration, permission catalogue, lockout, token, migration, and architecture tests.
## Edge Cases
Store-scoped role assignment and refresh-token reuse.
## Security Considerations
No plaintext or reversible credential material.
## Architectural Impact
Auth boundary used by every later stage.
## Definition of Done
No implementation task is recreated.
## Follow-up

# TASK-03-001 — Complete API platform

## Status
COMPLETE
## Stage
Stage 03
## Objective
Provide versioned API routing, ProblemDetails, OpenAPI, CQRS dispatch, validation, idempotency, and realtime foundations.
## Why
All feature endpoints must use one API-first execution path.
## Scope
Stage 03 deliverables and tests.
## Out of Scope
Feature-domain endpoints.
## Dependencies
Stage 02.
## Relevant Files
`docs/stages/STAGE-03-api-platform.md`, `docs/API_STANDARDS.md`, `src/`, `tests/`.
## References
Stage 03 acceptance and exit checklist; ADR-043–045.
## Implementation Requirements
Preserve dispatcher use, validation, error contracts, OpenAPI completeness, and idempotency semantics.
## Acceptance Criteria
Stage 03 is marked DONE with its documented green build/test and live OpenAPI evidence.
## Tests Required
API contract, dispatcher, ProblemDetails, OpenAPI, and architecture tests.
## Edge Cases
Duplicate idempotency keys and validation failures.
## Security Considerations
Do not bypass authentication/permission middleware.
## Architectural Impact
Cross-cutting API boundary.
## Definition of Done
No implementation task is recreated.
## Follow-up

# TASK-04-001 — Complete sync, backup, and restore foundation

## Status
COMPLETE
## Stage
Stage 04
## Objective
Implement outbox/inbox sync, HLC/conflict handling, encrypted snapshots, cloud restore, and related API contracts.
## Why
Vuma must trade offline and recover store data.
## Scope
Stage 04 deliverables and acceptance tests.
## Out of Scope
Per-company database fan-out introduced by Stage 06c.
## Dependencies
Stage 03.
## Relevant Files
`docs/stages/STAGE-04-sync-and-backup.md`, `docs/SYNC_AND_BACKUP.md`, `src/VumaRetail.Sync/`, `scripts/dr-drill.sh`, `tests/`.
## References
Stage 04 acceptance; ADR-046–049; `docs/API_STANDARDS.md` §5, §8–§9.
## Implementation Requirements
Preserve idempotent inbox/outbox processing, HLC conflict policy, encrypted backup, and restore drill behavior.
## Acceptance Criteria
Stage 04 is marked DONE with documented green suite, reversible migration, and passing DR drill.
## Tests Required
Sync integration, conflict, backup/restore, migration, API, and architecture tests.
## Edge Cases
Replay, conflict, partial batch, and restore failure.
## Security Considerations
Backup encryption and tenant isolation.
## Architectural Impact
Defines sync boundary later extended by 06c.
## Definition of Done
No implementation task is recreated.
## Follow-up

# TASK-04B-001 — Complete licensing and entitlement enforcement

## Status
COMPLETE
## Stage
Stage 04b
## Objective
Implement activation, signed lease, metering, entitlement gating, read-only ladder, emergency unlock, and support access.
## Why
Licensing is a platform invariant for all optional modules.
## Scope
All Stage 04b deliverables and acceptance tests.
## Out of Scope
Vendor control-plane implementation in Stage 30b.
## Dependencies
Stage 04.
## Relevant Files
`docs/stages/STAGE-04b-licensing.md`, `docs/LICENSING.md`, `src/`, `tests/`.
## References
Stage 04b business rules and exit checklist; ADR-028.
## Implementation Requirements
Preserve fail-closed lease behavior, no accidental lockout, read-only carve-outs, signed activation, and dual-audited support access.
## Acceptance Criteria
Stage 04b is marked DONE with its documented activation, ladder, read-only, unlock, and audit evidence.
## Tests Required
Licensing, metering, no-lockout, read-only, activation, support-access, and migration tests.
## Edge Cases
Network fault must not cause lockout; payment recovery must unlock quickly.
## Security Considerations
Hardware binding, signed licence validation, and no credential leakage.
## Architectural Impact
All optional-module commands depend on this gate.
## Definition of Done
No implementation task is recreated.
## Follow-up

# TASK-06-001 — Complete master data

## Status
COMPLETE
## Stage
Stage 06
## Objective
Implement items, variants, barcodes, units of measure, partners, pagination, permissions, replication registration, and seed data.
## Why
Catalogues and partners are prerequisites for finance, inventory, sales, imports, and procurement.
## Scope
All Stage 06 deliverables and acceptance tests.
## Out of Scope
Multi-company registry and future pricing documents.
## Dependencies
Stage 05 as specified by the roadmap; existing stage evidence records Stage 06 DONE.
## Relevant Files
`docs/stages/STAGE-06-master-data.md`, `docs/DATA_MODEL.md`, `src/`, `tests/`.
## References
Stage 06 Deliverables, business rules, and exit checklist.
## Implementation Requirements
Preserve attachment, uniqueness, keyset pagination, UoM, RBAC, replication, and entitlement requirements.
## Acceptance Criteria
Stage 06 is marked DONE with documented migration, tests, seed, and registry evidence.
## Tests Required
Domain/application, PostgreSQL migration, pagination, uniqueness, permissions, and read-only tests.
## Edge Cases
Duplicate barcode, invalid UoM conversion, and soft-deleted records.
## Security Considerations
Tenant-scoped partner and catalogue access.
## Architectural Impact
Provides entities consumed by later stages.
## Definition of Done
No implementation task is recreated.
## Follow-up

# TASK-07-001 — Complete finance core

## Status
COMPLETE
## Stage
Stage 07
## Objective
Implement GL, AR, AP, banking, tax, financial events, posting rules, permissions, and reconciliation foundations.
## Why
Every sale, receipt, stock movement, and later cross-company operation needs balanced accounting.
## Scope
All Stage 07 deliverables and acceptance tests.
## Out of Scope
Cross-company clearing in Stage 07c.
## Dependencies
Stage 06.
## Relevant Files
`docs/stages/STAGE-07-finance.md`, `docs/DATA_MODEL.md`, `docs/SYNC_AND_BACKUP.md`, `src/`, `tests/`.
## References
Stage 07 specification and ADR-016.
## Implementation Requirements
Preserve balanced postings, tax precision, append-only financial events, and posting-rule engine boundaries.
## Acceptance Criteria
Stage 07 is marked DONE with the documented build, test, migration, and posting evidence.
## Tests Required
Money/tax, posting, reconciliation, migration, API, permission, and read-only tests.
## Edge Cases
Rounding, foreign currency, reversals, and duplicate financial events.
## Security Considerations
Financial audit trail and tenant isolation.
## Architectural Impact
Accounting spine used by 07c and every trading module.
## Definition of Done
No implementation task is recreated.
## Follow-up

# TASK-08-001 — Complete inventory core

## Status
COMPLETE
## Stage
Stage 08
## Objective
Implement the append-only stock ledger, valuation, transfers, adjustments, stocktakes, permissions, and replication.
## Why
Inventory availability and all physical movement depend on one ledger.
## Scope
All Stage 08 deliverables and acceptance tests.
## Out of Scope
Warehouse bins and cross-company availability.
## Dependencies
Stage 07.
## Relevant Files
`docs/stages/STAGE-08-inventory-core.md`, `docs/DATA_MODEL.md`, `src/`, `tests/`.
## References
Stage 08 specification; ADR-005, ADR-007, ADR-016.
## Implementation Requirements
Preserve append-only movements, valuation, no-negative-stock rules, transfer/stocktake behavior, and posting integration.
## Acceptance Criteria
Stage 08 is marked DONE with documented green tests, reversible migration, and ledger evidence.
## Tests Required
Ledger, valuation, stocktake, transfer, concurrency, migration, and API tests.
## Edge Cases
Concurrent movement, reversal, zero quantity, and foreign currency cost.
## Security Considerations
Immutable stock/audit history and permission checks.
## Architectural Impact
Single stock source used by POS, warehouse, orders, and sourcing.
## Definition of Done
No implementation task is recreated.
## Follow-up

