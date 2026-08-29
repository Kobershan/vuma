# Task

## Status

NEEDS_VERIFICATION

## Stage

Stage 06c — Multi-company foundation

## Type

DOMAIN, INFRASTRUCTURE, DATABASE, TEST

## Objective

Add and backfill `company_id` on every existing business row while preserving one-company behavior.

## Why

Restores/exports must be self-describing and later projections need the redundant identity key.

## Scope

Entity/configuration inventory, non-null migration/backfill, indexes, invariants, query/sync regression tests.

## Out of Scope

New registry features, cross-company behavior, and unrelated refactors.

## Architecture

Layer: Domain/Infrastructure persistence. Data flow: active company context → owned row company key → query/projection/sync. Company DB owns rows; registry is not a foreign-key target.

## Architectural Boundaries

No cross-database foreign keys or client reassignment. API impact is unchanged DTO behavior; authorization remains tenant/company policy; sync metadata retains company identity.

## Dependencies

TASK-06C-04 and TASK-06C-08; ADR-099, ADR-119.

## Relevant Files

All existing business entities/configurations, company migrations, repositories, tests.

## Relevant Documentation

`docs/DATA_MODEL.md` §§2b–4, `docs/MULTI_COMPANY.md` §2, `docs/SECURITY.md`, ADR-099/119.

## Implementation Requirements

Inventory all business tables, backfill deterministically to the migrated company, make non-null, preserve sync and query behavior, and document exceptions.

## Data/Database Impact

Schema migration and backfill across company databases; indexes and consistency checks.

## API Impact

No new endpoint; reject client-controlled company mutation.

## Security

Company ID is not authorization; enforce context and tenant checks.

## Multi-Company/Tenant Impact

Rows are self-describing without weakening database isolation.

## Sync/Offline Impact

Serialization and replay retain company identity and never cross company queues.

## Acceptance Criteria

Every existing business row is correctly keyed; migration is safe/reversible where possible; existing tests remain green.

## Tests Required

Inventory architecture test, backfill/isolation, migration up/down, query and sync regression tests.

## Edge Cases

Empty/shared tables, null legacy data, duplicates, partial rollback, historical rows.

## Definition of Done

All scoped tables and exceptions are verified and documented.

## Follow-up Findings

New module tables must adopt the invariant in later stages.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-013.
- 2026-08-29: Completed the retrofit implementation: required EF mapping and migration backfill,
  tenant/company indexes, active-company query filtering, production row stamping, reassignment
  rejection, audit-row propagation, and migration-runner company identity setting. Scoped automated
  checks passed: unit company tests 11, architecture tests 35, and persistence/query/sync integration
  tests 70. The full integration command (`export VUMA_TEST_POSTGRES='Host=127.0.0.1;Port=55432;Database=postgres;Username=vuma;Password=vuma'; dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --no-restore`) ran 427 tests with 343 passed and 84 failures in unrelated procurement, registry, licensing, orders, finance, and API setup/contract areas; representative failure: expected pre-existing `ux_rfq_responses_rfq_id_partner_id` index name was absent. Per automation policy the task remains NEEDS_VERIFICATION.
