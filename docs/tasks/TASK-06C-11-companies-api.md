# Task

## Status

NOT_STARTED

## Stage

Stage 06c — Multi-company foundation

## Type

API, APPLICATION, SECURITY, TEST

## Objective

Expose company lifecycle, selection, and migration status through the versioned API.

## Why

Operators need a safe control surface for the registry and company context.

## Scope

`/api/v1/companies`, selection header/terminal binding, migration reporting, permissions, entitlement, OpenAPI.

## Out of Scope

Group APIs, cross-company business APIs, UI, and vendor control plane.

## Architecture

Layer: Web/API over Application commands/queries. Data flow: authenticated request → tenant/company authorization → registry/use case → DTO. Registry owns lifecycle; company DB remains business owner.

## Architectural Boundaries

DTOs never expose connection details; no API handler opens two company contexts. Auth/entitlement are mandatory; sync impact is selection metadata only.

## Dependencies

TASK-06C-06, TASK-06C-07, TASK-06C-08; `docs/API_STANDARDS.md`; ADR-099/118.

## Relevant Files

Web/StoreServer endpoints, Application commands/queries, Contracts, permission catalogue, API tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §§1–2, §9, `docs/API_STANDARDS.md`, `docs/SECURITY.md`, ADR-118.

## Implementation Requirements

Implement list/provision/activate/deactivate and migration status with consistent ProblemDetails, header validation, terminal binding, and OpenAPI.

## Data/Database Impact

Registry reads/writes only; company operation uses resolved one-company context.

## API Impact

Adds versioned lifecycle and migration endpoints plus selection contract.

## Security

Enforce permissions, tenant isolation, entitlement, audit, and secret redaction; avoid existence leaks.

## Multi-Company/Tenant Impact

No implicit access to another company; ordinary lists contain active/in-scope companies only.

## Sync/Offline Impact

Terminal binding wins over unsafe override; stale selection fails clearly.

## Acceptance Criteria

Authorized operations work; unauthorized/mismatched requests are rejected; OpenAPI and contract tests pass.

## Tests Required

Endpoint, permission, header/binding, entitlement, lifecycle, migration-status, and redaction tests.

## Edge Cases

No active companies, duplicate provision, deactivation race, missing header, stale token.

## Definition of Done

API, contracts, permissions, tests, and OpenAPI are green.

## Follow-up Findings

Group business endpoints belong to later stages.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-015.
