# Task

## Status
BLOCKED
NEEDS_VERIFICATION

## Stage

Stage 06c — Multi-company foundation

## Type

APPLICATION, INFRASTRUCTURE, DATABASE, SECURITY

## Objective

Build resumable company provisioning: create, migrate, seed, register, activate.

## Why

A failed setup must never expose a half-ready company.

## Scope

Command/orchestrator, lifecycle steps, progress/error records, database creation, migrations, seed hooks, resume/idempotency.

## Out of Scope

Public API, migration fleet runner, backup/sync, groups, and deactivation policy.

## Architecture

Layer: Application orchestration with Infrastructure adapters. Data flow: authorized command → registry state → company DB → seed → registry activation. Registry owns lifecycle; company DB owns seeded business configuration.

## Architectural Boundaries

Register only after migration/seed; no cross-database transaction. Authorization/entitlement required; API exposed later; sync starts only for Active company.

## Dependencies

TASK-06C-02 and TASK-06C-04; ADR-118.

## Relevant Files

Provisioning ports/services, migration/seed adapters, registry persistence, integration tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §9, `docs/DATA_MODEL.md` §4l, `docs/LICENSING.md`, ADR-099/118.

## Implementation Requirements

Make each step recorded and idempotent; resume from failure; seed chart, numbering, tax, permissions; activate last.

## Data/Database Impact

Create isolated company databases and seed required company-owned data.

## API Impact

Application command only; endpoint is TASK-06C-10.

## Security

Require management permission and entitlement; use secret references; redact failures.

## Multi-Company/Tenant Impact

Enforce tenant company limits and never activate across tenants.

## Sync/Offline Impact

No sync registration before activation; provisioning is online/control-plane work.

## Acceptance Criteria

Three companies provision independently; interruption after creation resumes without active interim state.

## Tests Required

Failure injection, resume/idempotency, isolation, seed, entitlement, and lifecycle race tests.

## Edge Cases

Existing DB, duplicate request, partial seed, registration failure, activation race, limit exceeded.

## Definition of Done

Resumable workflow and tests are green with no half-registered company.

## Follow-up Findings

External installer orchestration remains later-stage work.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-010.
- 2026-08-28: Added resumable registry progress/error metadata, tenant company-limit enforcement,
  idempotent registry re-drive, ordered lifecycle transitions, and registry migration for the new
  provisioning fields. Unit suite: 839 passed.
- 2026-08-28: `dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --no-restore`
  could not run because the harness reported no PostgreSQL/Docker service available; 24 unrelated
  tests passed before 403 infrastructure-dependent failures. Task remains NEEDS_VERIFICATION.
- 2026-08-28: Added explicit database-create, migrate, seed, and connection-registration adapter
  ports and ordered infrastructure steps, including secret-reference validation. Unit suite: 841
  passed; architecture suite: 35 passed. Integration suite attempted again and was blocked by Docker
  unavailable (`Docker is either not running or misconfigured`); 24 passed before 403 infrastructure
  failures. Task remains NEEDS_VERIFICATION.
- 2026-08-28: Verification rerun: `dotnet test tests/VumaRetail.UnitTests/VumaRetail.UnitTests.csproj --no-restore`
  passed (841); `dotnet test tests/VumaRetail.ArchitectureTests/VumaRetail.ArchitectureTests.csproj --no-restore`
  passed (35); `dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --no-restore`
  completed with 24 passed and 403 failures because Testcontainers reported `Docker is either not
  running or misconfigured` from `PostgresFixture`. Task remains NEEDS_VERIFICATION.

- BLOCKED after 3 attempts: automated validation still unavailable after remediation attempt 3/3; exit=0 status=NEEDS_VERIFICATION; see /home/kob/Documents/vuma/docs/automation/logs/20260828T213150Z-TASK-06C-06-attempt-3.log
