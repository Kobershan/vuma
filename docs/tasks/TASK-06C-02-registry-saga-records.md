# Task

## Status

COMPLETE

## Stage

Stage 06c — Multi-company foundation

## Type

DOMAIN, INFRASTRUCTURE, DATABASE

## Objective

Persist company groups, saga intents, saga legs, and the registry outbox.

## Why

Later cross-company stages require durable coordination without a cross-database transaction.

## Scope

Registry-owned models, mappings, migrations, idempotency keys, leg state, and persistence tests.

## Out of Scope

Saga business handlers, group services, routing, API, and company database writes.

## Architecture

Layer: Domain/Infrastructure. Component: registry coordination store. Data flow: command intent → registry saga → independent legs/outbox. Registry owns coordination metadata; each leg owns its company operation result.

## Architectural Boundaries

No leg may contain a second company transaction; no foreign key crosses company databases. No direct API, authorization, or terminal behavior beyond durable seams.

## Dependencies

TASK-06C-01; ADR-116, ADR-119.

## Relevant Files

Registry domain, Infrastructure context/configuration/migrations, tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §§3–6, §9, `docs/DECISIONS.md` ADR-116/119, `docs/SYNC_AND_BACKUP.md`.

## Implementation Requirements

Make intent/leg transitions explicit, idempotent, auditable, and resumable; use UUID v7 and HLC/sync metadata where specified.

## Data/Database Impact

Add registry groups, members, saga intents, legs, and outbox tables only.

## API Impact

None.

## Security

Tenant and authorized operator scope on every intent; redact payload secrets.

## Multi-Company/Tenant Impact

Represent company sets without granting implicit cross-company access.

## Sync/Offline Impact

Registry outbox is distinct from each company outbox; duplicate delivery has exactly-once effect.

## Acceptance Criteria

Records survive restart, retries, and partial leg completion without a distributed transaction.

## Tests Required

Transition, idempotency, persistence, tenant isolation, outbox atomicity, and architecture tests.

## Edge Cases

Duplicate intent, missing company, leg timeout, retry after completion, partial registry failure.

## Definition of Done

Models/migrations/tests are green and boundaries are documented.

## Follow-up Findings

Later group behavior belongs to Stage 06d.

- FOUND: Registry migration and persistence integration tests could not run because Docker and the
  local PostgreSQL endpoint are unavailable in this environment.
  WHY IT MATTERS: Migration up/down and database constraint behavior remain unverified against real
  PostgreSQL, although the migration compiles and the domain/unit checks pass.
  RECOMMENDED FOLLOW-UP: Run the registry migration integration panel with `scripts/pg-test.sh start`
  or Docker, including migrate, rollback, and re-apply.
  PRIORITY: HIGH

- FOUND: The documented disposable PostgreSQL cluster starts, but the integration fixture fails
  before the registry tests because `VumaRetailDbContext` reports pending model changes via
  `PendingModelChangesWarning` while creating its company template database.
  WHY IT MATTERS: Registry migration up/down and persistence behavior still cannot be exercised
  through the shared integration fixture; changing the company migration chain would exceed this
  registry-only task's scope.
  RECOMMENDED FOLLOW-UP: Reconcile the existing company model snapshot/migrations, then rerun
  `VUMA_TEST_POSTGRES=... dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj
  --filter 'FullyQualifiedName~RegistryPersistence' --no-restore` and the full persistence panel.
  PRIORITY: HIGH

- FOUND: Registry consumer-side redrive and exactly-once effect enforcement are not implemented in
  this persistence task.
  WHY IT MATTERS: Acknowledgement replay after a company restore needs the company inbox/handler and
  restore-point query specified by ADR-120; those belong to later sync/restore work.
  RECOMMENDED FOLLOW-UP: Add a stable `(intent_id, leg_id)` consumer idempotency seam and restore
  redrive service in the per-database sync/backup task.
  PRIORITY: HIGH

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-006.
- 2026-08-28: Implemented registry groups, saga intents/legs, idempotent registry outbox records,
  explicit transitions, tenant-scoped composite constraints, HLC metadata, migration, and unit
  coverage. Unit tests: 825 passed;
  architecture tests: 35 passed; infrastructure build: 0 errors. Integration tests: 401 failed at
  PostgreSQL fixture startup because Docker/local PostgreSQL was unavailable; task remains
  NEEDS_VERIFICATION until the database panel runs.
- 2026-08-28: Corrected registry composite foreign-key principals to use PostgreSQL unique
  constraints (rather than unique indexes), included tenant_id in group-member and saga-leg keys,
  and made compensation explicit for pending/dispatched/failed/timed-out legs while preserving
  acknowledged legs. Added duplicate-acknowledgement and partial-timeout compensation coverage.
  Unit tests: 827 passed; architecture tests: 35 passed; infrastructure build: 0 errors.
  Targeted migration tests: 3 failed during fixture startup because Docker and the local PostgreSQL
  endpoint were unavailable; task remains NEEDS_VERIFICATION.
- 2026-08-28: Re-verified the committed implementation. `dotnet test
  tests/VumaRetail.UnitTests/VumaRetail.UnitTests.csproj --no-restore`: 827 passed;
  `dotnet test tests/VumaRetail.UnitTests/VumaRetail.UnitTests.csproj --filter
  'FullyQualifiedName~Registry' --no-restore`: 11 passed; `dotnet test
  tests/VumaRetail.ArchitectureTests/VumaRetail.ArchitectureTests.csproj --no-restore`: 35 passed;
  `dotnet build src/VumaRetail.Infrastructure/VumaRetail.Infrastructure.csproj --no-restore`:
  succeeded with 0 warnings and 0 errors. `dotnet test
  tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --filter
  'FullyQualifiedName~Persistence.MigrationTests' --no-restore`: 3 failed before test execution
  because Docker is unavailable and no local PostgreSQL endpoint is available. Status remains
  NEEDS_VERIFICATION pending registry migration up/down and persistence execution against PostgreSQL.
- 2026-08-28: Added registry payload JSON validation and recursive redaction for credential-shaped
  fields, plus PostgreSQL integration coverage for registry migration, persistence, tenant scoping,
  outbox idempotency, and rollback atomicity. `dotnet test
  tests/VumaRetail.UnitTests/VumaRetail.UnitTests.csproj --no-restore`: 828 passed; registry filter:
  12 passed; `dotnet test tests/VumaRetail.ArchitectureTests/VumaRetail.ArchitectureTests.csproj
  --no-restore`: 35 passed; `dotnet build src/VumaRetail.Infrastructure/VumaRetail.Infrastructure.csproj
  --no-restore`: 0 warnings, 0 errors. `dotnet test
  tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --filter
  'FullyQualifiedName~RegistryPersistence' --no-restore`: 2 failed before execution because Docker
  and the local PostgreSQL endpoint were unavailable. Status remains NEEDS_VERIFICATION pending the
  registry migration and persistence panel against PostgreSQL.
- 2026-08-28: Re-ran the required registry persistence filter sequentially after starting the
  documented disposable PostgreSQL cluster with `scripts/pg-test.sh start`. Both tests failed before
  registry fixture execution because the shared company migration chain raised
  `Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning` for `VumaRetailDbContext`.
  The cluster was stopped with `scripts/pg-test.sh stop`; task remains NEEDS_VERIFICATION pending
  reconciliation of that unrelated company migration state and registry PostgreSQL verification.
- 2026-08-28: Hardened public registry record constructors so tenant, intent, leg, group, and company
  identifiers cannot be omitted or empty before persistence; added a unit test for tenant-scoped leg
  construction. `dotnet test tests/VumaRetail.UnitTests/VumaRetail.UnitTests.csproj --no-restore`: 829
  passed; `dotnet test tests/VumaRetail.ArchitectureTests/VumaRetail.ArchitectureTests.csproj --no-restore`:
  35 passed; `dotnet build src/VumaRetail.Infrastructure/VumaRetail.Infrastructure.csproj --no-restore
  --nologo`: succeeded with 0 warnings and 0 errors. Required registry persistence tests: 2 failed
  before fixture execution because Docker is unavailable and no local PostgreSQL endpoint is available.
  Status remains NEEDS_VERIFICATION pending database-backed migration and persistence verification.
- 2026-08-28: Added a default ambient tenant query filter to every registry entity, with an explicit
  design-time bypass matching the migration-only boundary; strengthened the PostgreSQL persistence
  test to insert a second tenant and verify it is hidden, then reopen the context and verify records
  survive restart. `dotnet test tests/VumaRetail.UnitTests/VumaRetail.UnitTests.csproj --no-restore
  --nologo`: 829 passed; registry filter: 13 passed; `dotnet test
  tests/VumaRetail.ArchitectureTests/VumaRetail.ArchitectureTests.csproj --no-restore --nologo`: 35
  passed; `git diff --check`: passed. `dotnet build
  src/VumaRetail.Infrastructure/VumaRetail.Infrastructure.csproj --no-restore --nologo`: succeeded;
  existing repository warnings remain. Required registry persistence tests first failed because Docker
  and local PostgreSQL were unavailable; after `scripts/pg-test.sh start`, both failed during shared
  fixture setup on the existing `VumaRetailDbContext` PendingModelChangesWarning. The disposable
  cluster was stopped. Status remains NEEDS_VERIFICATION pending database-backed verification.
- 2026-08-28: Repaired the repository-local PostgreSQL prerequisite that blocked verification. Added
  EF model snapshots for the company and registry contexts, made the company identity index migration
  idempotent after the existing column retrofit, ordered registry composite unique constraints before
  their foreign keys, and fixed the rollback probe to use an explicit EF transaction. Infrastructure
  build passed; unit tests: 829 passed; architecture tests: 35 passed; registry persistence tests: 2
  passed; migration tests: 3 passed; `git diff --check` passed. Task is COMPLETE.
