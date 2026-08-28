# Task

## Status

READY

## Stage

Stage 06c — Multi-company foundation

## Type

DOMAIN, INFRASTRUCTURE, DATABASE

## Objective

Create the tenant registry model, context, migration chain, and design-time wiring for `registry.companies`.

## Why

Company routing and lifecycle cannot be safe until company identity is stored separately from company databases.

## Scope

Company identity/lifecycle fields, encrypted connection metadata seam, registry DbContext, migration, DI, and tests.

## Out of Scope

Groups, saga records, routing, provisioning orchestration, APIs, and business-table retrofit.

## Architecture

Layer: Domain and Infrastructure. Component: registry persistence. Data flow: registry store → registry context → ports. Data ownership: registry owns company metadata; company DB owns business data.

## Architectural Boundaries

No business tables in the registry; no connection strings in application configuration; no company DB context from this task. Multi-company impact is foundational. Authorization, sync, and API impact are none beyond seams.

## Dependencies

Stage 01, Stage 04, Stage 06, Stage 07; ADR-099, ADR-118. No task dependency.

## Relevant Files

`src/VumaRetail.Domain/`, `src/VumaRetail.Infrastructure/`, migration and DI composition files discovered during implementation.

## Relevant Documentation

`docs/ARCHITECTURE.md`, `docs/MULTI_COMPANY.md` §§1–2, §9, `docs/DATA_MODEL.md` §4l, `docs/DECISIONS.md` ADR-099/118, `docs/API_STANDARDS.md`.

## Implementation Requirements

Use UUID v7, explicit lifecycle states, encrypted-at-rest connection secret abstraction, registry-first migration, and reversible migration where safe. Preserve one-company behavior.

## Data/Database Impact

Create the separate registry database/context and `companies` table with required indexes and schema state.

## API Impact

None; publish application ports only.

## Security

Never log, serialize, or export connection details; restrict registry access to platform services.

## Multi-Company/Tenant Impact

Establish tenant-owned company metadata without allowing cross-tenant company lookup.

## Sync/Offline Impact

Registry metadata is not terminal business cache; define no shared company outbox.

## Acceptance Criteria

Registry context migrates independently; company records persist all specified lifecycle/schema fields; existing one-company persistence remains compatible.

## Tests Required

Domain invariants, EF mapping/migration up/down, tenant isolation, secret-redaction, architecture dependency tests.

## Edge Cases

Duplicate code, invalid lifecycle transition, missing connection secret, empty registry, migration rerun.

## Definition of Done

Scoped source/tests and migration are green, ADR boundaries are preserved, and task evidence is recorded.

## Follow-up Findings

Record unrelated issues only; do not expand into routing or provisioning.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-005.
