# Task

## Status

NEEDS_VERIFICATION

## Stage

Stage 06c — Multi-company foundation

## Type

APPLICATION, SECURITY, API, TEST

## Objective

Make deactivated companies read-only while preserving history, reads, reprints, and permitted exports.

## Why

Trading must stop safely without deleting accounting or audit history.

## Scope

State transition, write guard, active filtering, audit, and allowed-read policy.

## Out of Scope

Physical removal, export workflow, licensing redesign, links, and migration runner.

## Architecture

Layer: Application authorization/policy over registry lifecycle. Data flow: authorized command → registry state → operation guard. Company DB remains owner of history.

## Architectural Boundaries

No delete and no implicit reactivation; fail closed on writes. API impact is error/permission seam; sync impact is pending-work policy, not new transport.

## Dependencies

TASK-06C-06; ADR-118.

## Relevant Files

Lifecycle commands/guards, permissions, API error contracts if required, tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §9, `docs/API_STANDARDS.md`, `docs/SECURITY.md`, ADR-118.

## Implementation Requirements

Require `company.manage`, audit actor/reason, reject mutations, retain policy-approved reads.

## Data/Database Impact

Registry lifecycle only; no history deletion.

## API Impact

Named authorization/business error for forbidden writes.

## Security

Separate management permission from business access; prevent horizontal access.

## Multi-Company/Tenant Impact

Inactive companies excluded from ordinary operations and fan-outs.

## Sync/Offline Impact

Define handling of queued writes and in-flight work as explicit failure/reconciliation, not silent replay.

## Acceptance Criteria

Deactivate is auditable/idempotent; writes fail; permitted reads/reprints/exports continue.

## Tests Required

Transition, authorization, allowed reads, concurrent write/deactivate, API contract tests.

## Edge Cases

Open till, in-flight saga, pending sync, repeated request, unknown company.

## Definition of Done

Guard and policy are tested and documented.

## Follow-up Findings

Removal/export remains a separate future task.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-011.
- 2026-08-29: Implemented idempotent deactivation with retained lifecycle metadata, actor/reason
  audit records, tenant-scoped company access modes, named `COMPANY_READ_ONLY` write refusal,
  separate company view/manage permissions, and versioned company lifecycle endpoints. Unit suite:
  841 passed. Integration command attempted:
  `dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --no-restore --verbosity minimal`.
  It could not initialize because Docker is unavailable and no local PostgreSQL fallback was
  configured (`Docker is either not running or misconfigured` / `No PostgreSQL available`).
  Task remains NEEDS_VERIFICATION until the required integration checks can execute.
