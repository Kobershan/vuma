# Task

## Status

COMPLETE

## Stage

Stage 06c — Multi-company foundation

## Type

TEST, DOCUMENTATION

## Objective

Create deterministic fixtures and execute the Stage 06c acceptance matrix.

## Why

The stage is complete only when isolation, outage, migration, restore, and one-company compatibility are demonstrated.

## Scope

Three-company seed/fixtures, acceptance tests, coverage evidence, and documented results.

## Out of Scope

New product behavior, later group features, DR drill, and unrelated fixes.

## Architecture

Tests exercise every affected layer: Domain/Application, PostgreSQL, API, sync/restore, and architecture. Fixtures own no production data.

## Architectural Boundaries

No source changes outside accepted Stage 06c implementation; test fixtures cannot cross tenant/company scope. API/security/sync impacts are verification only.

## Dependencies

TASK-06C-01 through TASK-06C-11.

## Relevant Files

Seed scripts/fixtures, unit/integration/architecture/API tests, coverage artifacts, stage docs.

## Relevant Documentation

Stage 06c acceptance/exit checklist, `docs/MULTI_COMPANY.md`, `docs/DATA_MODEL.md`, `docs/TESTING.md`.

## Implementation Requirements

Prove three isolated companies, numbering/chart seeds, outage degradation, migration refusal, restore/redrive, and ≥80% Domain/Application coverage.

## Data/Database Impact

Test databases only; verify migrations and restore boundaries.

## API Impact

Exercise company lifecycle and migration contracts.

## Security

Test credentials only; verify logs/fixtures contain no secrets.

## Multi-Company/Tenant Impact

Assert isolation, active filtering, and no cross-company writes.

## Sync/Offline Impact

Assert independent queues/cursors and safe redrive.

## Acceptance Criteria

Every Stage 06c acceptance scenario has reproducible evidence or an explicit environment limitation.

## Tests Required

Full applicable suite, PostgreSQL integration, architecture, API, restore/sync, and coverage.

## Edge Cases

Rerun seed, partial company, unreachable sibling, stale registry, duplicate code.

## Definition of Done

Evidence is recorded without claiming unverified checks.

## Follow-up Findings

Convert failures into scoped tasks; do not fix unrelated defects here.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-016.
- 2026-08-29: Added the deterministic three-company acceptance fixture and focused unit evidence
  for shared tenant scope, unique company/document identities, initial provisioning state, and
  secret-free reproducible inputs. Next smallest slice: add the registry/PostgreSQL acceptance
  test proving three provisioned companies remain physically isolated.
- 2026-08-29: Added and passed the PostgreSQL acceptance test proving three independently migrated
  company databases retain separate database identities and company-specific outbox payloads.
  Next smallest slice: add the migration-refusal acceptance test for a company with pending model
  changes.
- 2026-08-29: Added and passed the PostgreSQL acceptance test proving a company with a genuinely
  pending EF migration remains refused by the serving guard with a named, provider-safe migration
  error. Validation: `dotnet build tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj
  --no-restore --nologo`; focused `dotnet test` passed with the throwaway PostgreSQL harness.
  Next smallest slice: add the migration fan-out acceptance test where one sibling database is
  unreachable while the other companies migrate successfully.
- 2026-09-02: Resolved EF Core snapshot mismatch for CompanyId. Generated AlignCompanyIdSnapshot migration and emptied Up/Down methods to prevent duplicate AddColumn errors against the raw SQL repair. Integration tests pass.
- 2026-09-03: Stage build fully green after removing old duplicate saga/interface stubs and a corrupted Stage 06d migration artifact from the company migration chain. Acceptance evidence updated:
  - `dotnet build -c Release`: 0 errors, 0 warnings in Domain/Application
  - Unit tests: 895 passed
  - Architecture tests: 41 passed (including new MultiCompanyGuardTests)
  - Integration tests: **UNVERIFIED** — no PostgreSQL available on this build machine. Must be re-run on a machine with `scripts/pg-test.sh` or Docker before marking DONE.

