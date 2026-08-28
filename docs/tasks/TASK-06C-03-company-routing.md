# Task

## Status

COMPLETE

## Stage

Stage 06c — Multi-company foundation

## Type

APPLICATION, INFRASTRUCTURE, SECURITY

## Objective

Resolve an active company to its registry connection details with cache invalidation.

## Why

Company databases must never be selected from config or caller-controlled connection strings.

## Scope

`ICompanyConnectionResolver`, registry adapter, cache, invalidation, lifecycle/schema checks, and tests.

## Out of Scope

Context factory, fan-out, provisioning, APIs, and business handlers.

## Architecture

Layer: Application port and Infrastructure adapter. Data flow: authenticated company identity → registry → validated cached connection → one-company factory. Registry owns connection metadata.

## Architectural Boundaries

No connection config fallback; no cross-company context; selection is not authorization. API impact is none; sync/offline impact is cache invalidation only.

## Dependencies

TASK-06C-01; ADR-118.

## Relevant Files

Application abstractions, Infrastructure registry adapter/cache/DI, tests.

## Relevant Documentation

`docs/ARCHITECTURE.md`, `docs/MULTI_COMPANY.md` §§1–2, `docs/SECURITY.md`, ADR-118.

## Implementation Requirements

Fail closed for unknown, inactive, or schema-behind companies; invalidate on lifecycle/connection changes; redact secrets.

## Data/Database Impact

Read registry only; no new company business tables.

## API Impact

None.

## Security

Caller identity and tenant scope must be checked before resolution; never expose resolved details.

## Multi-Company/Tenant Impact

Prevent horizontal company selection across tenants.

## Sync/Offline Impact

Do not cache stale lifecycle or schema state beyond the defined invalidation policy.

## Acceptance Criteria

Resolver returns only valid in-scope active company details and refreshes after invalidation.

## Tests Required

Resolution, cache, invalidation, tenant isolation, redaction, inactive/schema-behind refusal.

## Edge Cases

Registry outage, stale cache, rotated secret, deleted company, concurrent invalidation.

## Definition of Done

Port/adapter/tests are green and no config fallback exists.

## Follow-up Findings

Serving guard details belong to TASK-06C-08.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-007.
- 2026-08-28: Implemented tenant-keyed registry resolution with a five-minute cache, fail-closed
  lifecycle/schema validation, and generation-safe invalidation. Lifecycle, provisioning, and migration
  paths evict routing entries after registry changes. Unit (829) and architecture (35) suites pass.
  Registry integration checks could not execute: Docker is unavailable; the local PostgreSQL fallback
  then stopped in the shared fixture on the existing `VumaRetailDbContext` pending-model-changes
  prerequisite. Exact commands: `dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj
  --filter 'FullyQualifiedName~Registry' --no-restore --nologo`; with
  `VUMA_TEST_POSTGRES='Host=127.0.0.1;Port=55432;Database=postgres;Username=vuma;Password=vuma'`.
- 2026-08-28: Remediated the verification blocker by starting the repository's documented local
  PostgreSQL fallback (`scripts/pg-test.sh start`). Registry integration checks pass (2), migration
  integration checks pass (4), unit tests pass (829), and architecture tests pass (35).
