# TASK-16-03 — BOM application commands, permissions, and API

Status: IN_PROGRESS  
Stage: 16 — BOM Setup  
Type: Application / Web

## Objective

Expose tenant-scoped BOM authoring, publication, and reading through the normal CQRS and API boundaries.

## Why

The domain and database cannot be operated safely by a host if callers can bypass validation,
licensing, permissions, and the unit-of-work pipeline.

## Scope

Application ports, create/publish/read handlers, permissions, module manifest, EF repository, and
initial authenticated API routes.

## Out of Scope

Graph-loading cost endpoint, production execution, seed, sync replay review, and final stage verification.

## Architecture

Commands flow through `IDispatcher`; handlers use `IBillOfMaterialsRepository`; endpoints map DTOs and
never call `DbContext` directly. Manufacturing holds opaque Catalog IDs and no cross-schema foreign keys.

## Acceptance Criteria

- Create, read, and publish routes are licensed and permission-gated.
- Command validation protects IDs, names, versions, quantities, units, and scrap.
- Repository reads are tenant-scoped by the shared EF query filter.
- Module permissions and manifest are registered through the module DI extension.

## Tests Required

Focused handler/endpoint tests and a real API integration path remain required before this task is complete.

## Definition of Done

Initial implementation compiles with 0 errors. Handler-focused tests pass 3/3; task remains `IN_PROGRESS`
until API behavior, authorization, and tenant isolation are tested.

## Follow-up Findings

- Add an explosion/cost query handler that loads the complete published graph and cost source.
- Add API integration tests for 201, 204, 404, 409, and 422 behavior.

## Work Log

- 2026-09-10: Added commands, repository, module manifest/permissions, contracts, routes, DI, and StoreServer wiring. StoreServer build passed with 0 errors.
- 2026-09-10: Added handler tests for tenant draft creation, duplicate-version refusal, and publication; 3/3 passed.
- 2026-09-10: Real PostgreSQL migration verification passed 1/1 in `ManufacturingMigrationTests`; API behavior and authorization integration coverage remain open.
- 2026-09-10: Real-host API tests now cover authorized create/read/publish and denied create; 2/2 passed. The documented 404/409/422 matrix and OpenAPI assertion remain open.
