# Task

## Status

NEEDS_VERIFICATION

## Stage

Stage 06c — Multi-company foundation

## Type

INFRASTRUCTURE, DATABASE, APPLICATION, TEST

## Objective

Run registry-first migrations across companies with bounded progress, resume, and serving refusal.

## Why

Partial upgrades are expected; serving a company against an unknown schema is unsafe.

## Scope

Migration runner, per-company state, bounded concurrency, retry/resume, serving guard, up/down tests.

## Out of Scope

Business-table retrofit, provisioning seed content, restore, and administration endpoints.

## Architecture

Layer: Infrastructure migration plus Application serving guard. Data flow: binary → registry → company DB migrations → resolver guard. Each DB owns its schema history.

## Architectural Boundaries

Registry migrates first; no cross-database transaction; API reports later. Authorization requires `platform.migrate`; sync waits for compatible schema.

## Dependencies

TASK-06C-01, TASK-06C-03, TASK-06C-06; ADR-117.

## Relevant Files

Migration services, registry state, serving pipeline, migration integration tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §9, `docs/DATA_MODEL.md` §4l, `docs/API_STANDARDS.md`, ADR-117.

## Implementation Requirements

Record pending migration and failures; refuse behind companies with actionable named reason; do not execute client-selected migrations.

## Data/Database Impact

Per-company schema version/state and registry migration state.

## API Impact

Internal status seam; administration endpoint is TASK-06C-10.

## Security

Restrict migration control and redact connection diagnostics.

## Multi-Company/Tenant Impact

One unreachable company must not block siblings, but remains unservable.

## Sync/Offline Impact

Do not sync against incompatible schema; resume safely after outage.

## Acceptance Criteria

Registry-first, bounded fan-out, outage recording, serving refusal, retry, and per-company up/down are proven.

## Tests Required

Runner ordering/concurrency, outage, refusal, resume, migration up/down integration tests.

## Edge Cases

Registry outage, timeout, mixed versions, cancellation, failed down migration.

## Definition of Done

Migration behavior is safe, resumable, tested, and reviewable.

## Follow-up Findings

Full fleet operational tooling is outside this stage.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-012.
- 2026-08-29: Implemented registry-first, bounded and resumable company migration fan-out. Company
  databases are migrated directly from their registered secret (without the serving-only resolver),
  registry state is marked Pending before fan-out, successful versions come from EF migration history,
  and failures are redacted while retaining the pending migration name for the serving guard.
- 2026-08-29: Unit registry suite passed (25 tests); architecture suite passed (35 tests). Required
  PostgreSQL migration integration tests could not run because Docker is unavailable and
  `VUMA_TEST_POSTGRES` is not configured. Exact command: `dotnet test
  tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --no-restore --filter
  'FullyQualifiedName~MigrationTests'`. Result: all 3 tests failed during fixture initialization with
  `No PostgreSQL available for the integration tests` / Docker endpoint unavailable. Remains
  NEEDS_VERIFICATION.
