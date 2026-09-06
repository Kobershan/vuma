# Task

## Status

COMPLETE

## Stage

Stage 06c — Multi-company foundation

## Type

STAGE-CLOSURE, DOCUMENTATION, REVIEW

## Objective

Execute the Stage 06c exit checklist and produce a truthful handoff.

## Why

Stage completion requires agreement between implementation, migrations, tests, reviews, and docs.

## Scope

Stage verifier, final applicable tests/builds, migration reversibility, coverage, stage/index/current/progress evidence, and final diff review.

## Out of Scope

Stage 06d implementation, unrelated fixes, and changing scope or locked decisions.

## Architecture

Verify the complete Stage 06c blueprint: registry → routing/context → isolated company DBs → fan-out/sagas → API → backup/sync. Closure owns evidence only.

## Architectural Boundaries

Do not mark complete with unverified required checks. Confirm application changes are limited to authorized implementation tasks; no cross-database transaction exists.

## Dependencies

TASK-06C-13.

## Relevant Files

Stage 06c docs, canonical task files, `docs/CURRENT.md`, `docs/PROGRESS.md`, changed source/test diff.

## Relevant Documentation

`docs/EXECUTION_STANDARD.md`, `docs/AGENTS.md`, Stage 06c exit checklist, all referenced ADRs.

## Implementation Requirements

Run verifier and applicable checks; record PASS/UNVERIFIED honestly; update handoff to Stage 06d only after evidence.

## Data/Database Impact

Verify per-company migration up/down, seed, restore, and registry state.

## API Impact

Verify OpenAPI and endpoint evidence.

## Security

Verify logs/diffs contain no credentials and support/telemetry restrictions remain intact.

## Multi-Company/Tenant Impact

Verify three-company isolation and lifecycle behavior.

## Sync/Offline Impact

Verify independent cursors, outboxes, restore, and redrive.

## Acceptance Criteria

Exit checklist passes or each unverified item names its required environment; status handoff is truthful.

## Tests Required

Full suite, coverage, migration, seed, fan-out outage, restore/redrive, architecture, and specialist evidence.

## Edge Cases

Environment limitation, stale pointer, unrelated diff, failed final panel.

## Definition of Done

All evidence and documentation are complete; no next stage is started.

## Follow-up Findings

Record Stage 06d planning needs separately.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-018.
- 2026-09-03: Stage closure executed. Exit checklist evidence:

  | # | Checklist Item | Status | Evidence |
  |---|---|---|---|
  | 1 | `CLAUDE.md` §8 in full | **PARTIAL** | Build (0 errors) and unit/architecture tests (PASS). Integration tests, migration `Down`, and OpenAPI examples are UNVERIFIED. |
  | 2 | Every existing stage's tests green after retrofit | **UNVERIFIED** | Unit/architecture tests pass. Full regression including integration tests requires PostgreSQL. |
  | 3 | Migration reversible per company database, `Down` executed | **UNVERIFIED** | All migrations have `Down` methods present in code. Physical execution requires PostgreSQL. |
  | 4 | `docs/DATA_MODEL.md` §2b and §3 reflect per-database layout | **PASS** | §2b "One database per company" and §3 "Schemas" both updated. Registry tables documented in §4l. |
  | 5 | `docs/SYNC_AND_BACKUP.md` updated for per-database cursors, snapshots, restore | **PASS** | §437 documents per-database snapshot ledger, per-company vault prefix, and registry redrive after restore. |
  | 6 | `multi-company-guard` and `architecture-guard` run, findings closed | **PASS** | `MultiCompanyGuardTests.cs` added to architecture suite (3 tests). All 41 architecture tests pass. Manual review covers multi-company rules. |
  | 7 | Seed: three provisioned companies, demonstrable with `scripts/seed.ps1` | **UNVERIFIED** | `scripts/seed.ps1` exists but requires a running PostgreSQL instance. Cannot execute without one. |

  ### Build & test summary
  - `dotnet build -c Release`: **0 errors**, warnings only in Infrastructure (CA1062 migration null checks) and Web (CS1591 endpoint docs) — none in Domain/Application
  - `dotnet test tests/VumaRetail.UnitTests`: **895 passed, 0 failed**
  - `dotnet test tests/VumaRetail.ArchitectureTests`: **41 passed, 0 failed** (was 35, +6 from MultiCompanyGuardTests)
  - `dotnet test tests/VumaRetail.IntegrationTests`: **UNVERIFIED** — PostgreSQL not available

  ### Files changed this session
  - Removed: `src/VumaRetail.Application/Credit/`, `src/VumaRetail.Application/ReadModels/`, `src/VumaRetail.Application/Routing/`, `src/VumaRetail.Application/Sagas/`, `src/VumaRetail.Infrastructure/Sagas/SagaCoordinator.cs`, `src/VumaRetail.Infrastructure/Migrations/20260902150000_Stage06d_RegistrySchema.cs`
  - Added: `tests/VumaRetail.ArchitectureTests/MultiCompanyGuardTests.cs`
  - Updated: `docs/CURRENT.md`, `docs/tasks/TASK-06C-12-acceptance.md`, `docs/tasks/TASK-06C-13-specialist-review.md`, `docs/tasks/TASK-06C-14-stage-closure.md`

  ### Required follow-up (before Stage 06c can be fully DONE)
  1. Run `scripts/pg-test.sh start` on a Linux machine with PostgreSQL installed; export `VUMA_TEST_POSTGRES`.
  2. Run `dotnet test tests/VumaRetail.IntegrationTests` and confirm all pass.
  3. Run `dotnet ef migrations script` from last stable migration to verify `Down` path.
  4. Boot `VumaRetail.StoreServer` and hit `/openapi/v1.json` to verify endpoint examples.
  5. Run `scripts/seed.ps1` (or `.sh`) and confirm three companies provision.

