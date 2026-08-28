# Task

## Status

NOT_STARTED

## Stage

Stage 06c — Multi-company foundation

## Type

APPLICATION, INTEGRATION, TEST

## Objective

Implement bounded-parallelism read fan-out with a result or named failure for every company.

## Why

Roll-up reads must degrade per company rather than become an opaque 500.

## Scope

`ICompanyFanOut`, bounded concurrency, result/error shape, cancellation, and integration tests.

## Out of Scope

Cross-company writes, group projections, APIs, and saga handlers.

## Architecture

Layer: Application orchestration over one-company reads. Data flow: authorized company set → independent contexts → per-company result/error. Ownership remains in each company DB.

## Architectural Boundaries

Read-only fan-out only; no shared transaction or context. Multi-company/auth impact is explicit scope filtering; sync/offline impact none.

## Dependencies

TASK-06C-04; ADR-116.

## Relevant Files

Application ports/services and integration tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §4, `docs/TESTING.md`, ADR-116.

## Implementation Requirements

Bound concurrency, preserve deterministic company identity, return failures without leaking secrets, honor cancellation.

## Data/Database Impact

Read-only access to separate company databases.

## API Impact

Define internal result shape; no endpoint.

## Security

Filter requested companies by tenant and permission before fan-out.

## Multi-Company/Tenant Impact

One unavailable company must not suppress sibling results.

## Sync/Offline Impact

No offline write or replay behavior.

## Acceptance Criteria

Three companies with one stopped return two results and one named error, with bounded concurrency.

## Tests Required

Concurrency, timeout, cancellation, partial failure, authorization, and integration tests.

## Edge Cases

Empty set, duplicate company, registry outage, one slow company, all unavailable.

## Definition of Done

Fan-out is deterministic, bounded, tested, and documented.

## Follow-up Findings

Group read models belong to Stage 06d.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-009.
