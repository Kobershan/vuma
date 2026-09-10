# TASK-16-04 — BOM explosion, rolled-up costing, and verification

Status: IN_PROGRESS  
Stage: 16 — BOM Setup  
Type: Application / Verification

## Objective

Provide deterministic multi-level BOM explosion and rolled-up costing for planning and future
manufacturing consumers.

## Why

Definitions are useful only when the required leaf quantities and cost can be calculated consistently
without silently accepting cycles, stale drafts, alternates, or mixed currencies.

## Scope

- Pure application explosion and costing engine.
- Cycle detection, alternate choice, scrap adjustment, and money safety.
- Focused unit coverage.

## Out of Scope

API commands and queries, permission registration, seed data, production execution, stock consumption,
and final Stage 16 exit verification.

## Architecture

The engine consumes published domain definitions and opaque Catalog IDs through dictionaries supplied
by an application adapter. It does not query another module or write downstream documents.

## Architectural Boundaries

No cross-schema navigation or foreign key is introduced. `Quantity` and `Money` remain the only numeric
types for component quantity and cost.

## Dependencies

`BillOfMaterials`, `Quantity`, and `Money` from the Domain project.

## Relevant Files

- `src/VumaRetail.Application/Manufacturing/BomExplosionEngine.cs`
- `src/VumaRetail.Domain/Manufacturing/ManufacturingRuleException.cs`
- `tests/VumaRetail.UnitTests/Manufacturing/BomExplosionEngineTests.cs`

## Acceptance Criteria

- Nested published definitions are recursively expanded to leaves.
- Scrap increases required quantity using precise decimal arithmetic.
- Alternate groups are deterministic and permit explicit selection.
- Cycles, unpublished nested definitions, missing costs, and mixed currencies fail with stable codes.

## Tests Required

Four unit tests for nested costing, alternate selection, cycles, and currency safety.

## Definition of Done

Implementation and focused tests pass. API, seed, and full stage verification remain follow-ups.

## Follow-up Findings

- Add an application handler and API surface that loads the graph from persistence.
- Decide whether a future production-specific alternate policy should require an explicit selection.

## Work Log

- 2026-09-10: Added `BomExplosionEngine` and four focused tests; all 4 passed.
