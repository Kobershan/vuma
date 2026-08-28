# Task

## Status

NOT_STARTED

## Stage

Stage 06c — Multi-company foundation

## Type

DOMAIN, INFRASTRUCTURE, DATABASE

## Objective

Persist company groups, saga intents, saga legs, and the registry outbox.

## Why

Later cross-company stages require durable coordination without a cross-database transaction.

## Scope

Registry-owned models, mappings, migrations, idempotency keys, leg state, and persistence tests.

## Out of Scope

Saga business handlers, group services, routing, API, and company database writes.

## Architecture

Layer: Domain/Infrastructure. Component: registry coordination store. Data flow: command intent → registry saga → independent legs/outbox. Registry owns coordination metadata; each leg owns its company operation result.

## Architectural Boundaries

No leg may contain a second company transaction; no foreign key crosses company databases. No direct API, authorization, or terminal behavior beyond durable seams.

## Dependencies

TASK-06C-01; ADR-116, ADR-119.

## Relevant Files

Registry domain, Infrastructure context/configuration/migrations, tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §§3–6, §9, `docs/DECISIONS.md` ADR-116/119, `docs/SYNC_AND_BACKUP.md`.

## Implementation Requirements

Make intent/leg transitions explicit, idempotent, auditable, and resumable; use UUID v7 and HLC/sync metadata where specified.

## Data/Database Impact

Add registry groups, members, saga intents, legs, and outbox tables only.

## API Impact

None.

## Security

Tenant and authorized operator scope on every intent; redact payload secrets.

## Multi-Company/Tenant Impact

Represent company sets without granting implicit cross-company access.

## Sync/Offline Impact

Registry outbox is distinct from each company outbox; duplicate delivery has exactly-once effect.

## Acceptance Criteria

Records survive restart, retries, and partial leg completion without a distributed transaction.

## Tests Required

Transition, idempotency, persistence, tenant isolation, outbox atomicity, and architecture tests.

## Edge Cases

Duplicate intent, missing company, leg timeout, retry after completion, partial registry failure.

## Definition of Done

Models/migrations/tests are green and boundaries are documented.

## Follow-up Findings

Later group behavior belongs to Stage 06d.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-006.
