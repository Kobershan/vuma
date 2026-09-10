# Task

## Status

NOT_STARTED

## Stage

Stage 19 — CRM verification

## Type

TESTING, DOCUMENTATION

## Objective

Prove TASK-19-001 meets the stage acceptance + CLAUDE.md §8: full suites green on real PG,
arch tests green (esp. wall-clock, no-context-in-handler, replication, persistence rules),
coverage ≥80% on new Domain+Application, migration Down verified, seed demonstrable,
specialist self-review findings closed.

## Why

Code without executed evidence is not DONE (TESTING.md: "It compiles is not evidence").

## Scope

- `tests/VumaRetail.UnitTests/Crm/` (updated scaffolding + new rule tests).
- `tests/VumaRetail.IntegrationTests/Crm/` (new: handler + 360-view tests on PG harness).
- Coverage measurement (coverlet) for `VumaRetail.Domain` Crm + `VumaRetail.Application`
  Crm namespaces; close gaps.
- `scripts/seed` extension: one demo lead → converted, one opportunity, one consent row.
- Docs: DATA_MODEL.md §crm, SYNC_AND_BACKUP.md registry rows, PROGRESS.md evidence.

## Out of Scope

Stage 20 testing (TASK-20-002); performance budgets (Stage 31).

## Architecture

Tests follow CONVENTIONS.md §7 (rule names, AAA, one behaviour per test, fixed Bogus
seeds). Integration via `PostgresCollection` + real migrations (no mocked DbContext).

## Dependencies

TASK-19-001 COMPLETE.

## Relevant Files

`tests/VumaRetail.IntegrationTests/Harness/PostgresFixture.cs`,
`tests/VumaRetail.IntegrationTests/FieldSales/FieldSalesHarness.cs` (pattern).

## Relevant Documentation

TESTING.md §1–§2, stage doc §Tests.

## Implementation Requirements

- Consumer-driven tests for the three Stage-20 contracts.
- Migration Up/Down round-trip test on scratch PG (follow Stage 10c precedent).

## Data/Database Impact

None (test-only + seed).

## API Impact

None.

## Security

Permission tests: no-permission → 403; other-tenant → 404.

## Multi-Company/Tenant Impact

Tenant-isolation test on crm tables.

## Sync/Offline Impact

Registry rows asserted present (replication-rules pattern).

## Acceptance Criteria

- Unit + integration + arch suites green; coverage ≥80%; seed proves on scratch DB.

## Tests Required

This task IS tests; plus the migration round-trip.

## Edge Cases

- Full-disk/cluster contention: use `VUMA_TEST_PG_PORT`/`DATA` overrides if needed.

## Definition of Done

Evidence recorded in PROGRESS.md; CURRENT.md updated; committed + pushed with 19-001.

## Follow-up Findings

(none yet)

## Work Log

- 2026-09-10: task written.
