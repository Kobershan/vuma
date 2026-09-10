# TASK-16-02 — BOM persistence and migration

Status: COMPLETE  
Stage: 16 — BOM Setup  
Type: Infrastructure / Database

## Objective

Persist versioned BOM definitions and routing steps in the manufacturing schema with reversible EF migrations.

## Scope

`manufacturing.bills_of_materials`, JSONB component/routing snapshots, tenant/version indexes, DbContext
registration, and real PostgreSQL migration evidence.

## Out of Scope

Application commands, API authorization, production execution, seed, and final stage closure.

## Architecture

The table derives from the shared `Entity` mapping. Catalog IDs remain opaque and no cross-schema foreign
keys are introduced. Component and routing collections are immutable snapshots stored as JSONB.

## Acceptance Criteria

- Migration Up creates the manufacturing table and indexes.
- Migration Down removes the manufacturing schema objects.
- `lines` and `routing_steps` are persisted as JSONB.

## Tests Required

Real PostgreSQL migration Up/Down integration test.

## Definition of Done

Migration generated, model snapshot updated, infrastructure builds with 0 errors, and PostgreSQL test passes.

## Follow-up Findings

None for this task. Graph loading and API behavior are tracked by TASK-16-03 and TASK-16-04.

## Work Log

- 2026-09-10: Added `20260910170012_Stage16_BomSetup` and `20260910170958_Stage16_RoutingSteps`.
- 2026-09-10: Real PostgreSQL migration test passed 1/1; manufacturing schema removed successfully on Down.
