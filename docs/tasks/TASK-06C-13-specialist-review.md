# Task

## Status

NOT_STARTED

## Stage

Stage 06c — Multi-company foundation

## Type

REVIEW

## Objective

Run architecture, multi-company, and sync/offline specialist reviews and close critical findings.

## Why

Boundary failures can pass feature tests while violating the product's database and sync invariants.

## Scope

Review all Stage 06c changes, require worked examples, regression tests for findings, and record limitations.

## Out of Scope

Stage 06d review, unrelated refactors, and changing locked ADRs silently.

## Architecture

Review the Domain/Application/Infrastructure/host boundaries, registry/company wall, saga legs, and sync isolation. Review owns findings, not implementation.

## Architectural Boundaries

No reviewer may approve a cross-database transaction or secret exposure. Auth, tenant, API, and offline claims must be evidenced.

## Dependencies

TASK-06C-12; ADR-099, ADR-116–120.

## Relevant Files

All changed Stage 06c files, architecture tests, review reports.

## Relevant Documentation

`docs/AGENTS.md`, Stage 06c exit checklist, `docs/MULTI_COMPANY.md` §11, `docs/SYNC_AND_BACKUP.md`.

## Implementation Requirements

Run applicable reviewers; classify findings; require fixes or accepted limitations with follow-up.

## Data/Database Impact

Verify separate migration, restore, and transaction boundaries.

## API Impact

Verify permission, DTO, error, and selection contracts.

## Security

Verify horizontal isolation, redaction, and restricted operations.

## Multi-Company/Tenant Impact

Verify every company boundary and saga leg is explicit.

## Sync/Offline Impact

Verify outbox/inbox/cursor isolation and idempotent restore redrive.

## Acceptance Criteria

No open critical/high boundary or sync findings; all limitations are explicit.

## Tests Required

Reviewer regression tests and rerun of affected suites.

## Edge Cases

Unavailable reviewer/tooling, locked ADR conflict, dirty unrelated diff.

## Definition of Done

Panel evidence and closed findings are recorded.

## Follow-up Findings

Create later tasks for non-blocking findings.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-017.
