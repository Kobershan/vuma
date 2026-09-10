# TASK-16-01 — BOM domain lifecycle and invariants

Status: IN_PROGRESS  
Stage: 16 — BOM Setup  
Type: Domain

## Objective

Introduce the tenant-scoped BOM aggregate and protect its draft/published/retired lifecycle,
component quantities, self-reference, alternates, and scrap rules.

## Why

Stage 17 manufacturing and Stage 15 planning need a stable definition boundary. A published BOM
must be immutable so historical costing and production instructions remain reproducible.

## Scope

- `BillOfMaterials` aggregate and component value records.
- Lifecycle and input invariants.
- Unit tests for the domain rules.

## Out of Scope

Persistence, API, permissions, multi-level explosion, rolled-up costing, routings, seed, and Stage 17 execution.

## Architecture

Domain-only, with `Quantity` for component quantities and no cross-schema foreign keys.

## Architectural Boundaries

Item and variant identifiers remain opaque IDs. Manufacturing does not reach into Catalog or Inventory.

## Dependencies

Catalog item identity and the repository's `Entity`, `Quantity`, and `DomainException` primitives.

## Relevant Files

- `src/VumaRetail.Domain/Manufacturing/BillOfMaterials.cs`
- `src/VumaRetail.Domain/Manufacturing/ManufacturingRuleException.cs`
- `tests/VumaRetail.UnitTests/Manufacturing/BillOfMaterialsTests.cs`

## Acceptance Criteria

- A BOM is draft on creation and requires at least one positive component to publish.
- Published definitions cannot be edited; retired definitions remain historical.
- Self-reference, invalid scrap, and invalid quantities fail with stable codes.
- Alternate group metadata is retained.

## Tests Required

Four focused unit tests covering publication, immutability, invalid inputs, and alternates.

## Definition of Done

Implementation compiles, focused tests pass, and this work log records the verification. Remaining
Stage 16 work stays open in the stage index.

## Follow-up Findings

- Persistence must add same-schema BOM tables and a migration with real PostgreSQL Up/Down evidence.
- Variant-level self-reference and catalog existence validation belong at the application boundary.

## Work Log

- 2026-09-10: Added aggregate, stable rule codes, and four unit tests. Focused verification pending.
