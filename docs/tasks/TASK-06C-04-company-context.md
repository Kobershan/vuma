# Task

## Status

COMPLETE

## Stage

Stage 06c — Multi-company foundation

## Type

APPLICATION, INFRASTRUCTURE, SECURITY, TEST

## Objective

Introduce the acting company context and a factory that opens exactly one company DbContext.

## Why

Company scope must be structural so callers cannot forget it.

## Scope

`ICompanyContext`, session/terminal binding resolution, `ICompanyDbContextFactory`, DI, and architecture guard.

## Out of Scope

Fan-out, provisioning, APIs, retrofit, and group services.

## Architecture

Layer: Application/Infrastructure. Data flow: tenant-authenticated session/device → company context → resolver → one DbContext. Company database owns business transaction data.

## Architectural Boundaries

One handler/context per transaction; no cross-company transaction. Authorization impact is company access; API impact is future selection seam; sync/offline impact is terminal binding.

## Dependencies

TASK-06C-03; ADR-099, ADR-116, ADR-118.

## Relevant Files

Application context abstractions, Infrastructure factory/DI, architecture and unit tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §§1–2, §9, `docs/SECURITY.md`, `docs/TESTING.md`, ADR-116.

## Implementation Requirements

Reject absent/mismatched context; make company identity unavailable as an optional forgotten parameter; enforce one-context architecture rule.

## Data/Database Impact

No schema change; factory targets existing company database context.

## API Impact

No endpoint; establish request context seam.

## Security

Terminal binding cannot be overridden by an untrusted header; authorization precedes context creation.

## Multi-Company/Tenant Impact

Every business operation has one explicit acting company.

## Sync/Offline Impact

Terminal queue entries carry company identity and cannot replay into another company.

## Acceptance Criteria

A handler receives one context; deliberately resolving two contexts fails architecture tests.

## Tests Required

Context resolution, mismatch, factory isolation, architecture dependency and terminal binding tests.

## Edge Cases

Missing company, stale session, inactive company, nested context, concurrent requests.

## Definition of Done

Context/factory and guard are green and documented.

## Follow-up Findings

API header contract belongs to TASK-06C-10.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-008.
- 2026-08-28: Hardened the existing company context and factory: empty, mismatched and nested
  bindings fail closed; factory creation requires an authenticated tenant and serving authorization,
  validates the resolver result, and rejects a second company DbContext in one operation. Added focused
  context/factory tests. Unit tests (834) and architecture tests (35) pass.
