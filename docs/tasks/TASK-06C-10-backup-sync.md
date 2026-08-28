# Task

## Status

NOT_STARTED

## Stage

Stage 06c — Multi-company foundation

## Type

INFRASTRUCTURE, INTEGRATION, SECURITY, TEST

## Objective

Isolate snapshots, restore, cursors, outboxes, inboxes, and cloud layout per company plus registry.

## Why

One company must be restorable or offline without stopping siblings.

## Scope

Snapshot ledger/schedules/retention/encryption metadata, isolated restore seam, registry redrive, sync registration/cursors/layout.

## Out of Scope

Stage 31 DR drill, new conflict policy, saga handlers, and cloud redesign.

## Architecture

Layer: Infrastructure/Sync. Data flow: company DB or registry → isolated snapshot/sync stream → cloud mirror; ownership follows source database.

## Architectural Boundaries

No shared cursor/outbox and no restore of sibling data. API impact is operational seam only; auth restricts restore; offline replay remains company-scoped.

## Dependencies

TASK-06C-02, TASK-06C-04, TASK-06C-09; ADR-120.

## Relevant Files

Backup/sync contracts and adapters, registry ledger, company context registration, integration tests.

## Relevant Documentation

`docs/MULTI_COMPANY.md` §9, `docs/SYNC_AND_BACKUP.md`, `docs/SECURITY.md`, ADR-006/007/120.

## Implementation Requirements

Track database identity in snapshots, keep registry cadence distinct, restore one company independently, and redrive idempotently.

## Data/Database Impact

Snapshot ledger and per-database sync metadata; no merged business store.

## API Impact

No public endpoint; expose ports for later operations.

## Security

Encrypt snapshots, restrict restore, redact credentials and telemetry.

## Multi-Company/Tenant Impact

Sibling availability and isolation are explicit acceptance properties.

## Sync/Offline Impact

Company cursors/outbox/inbox remain independent through outages and restore.

## Acceptance Criteria

Restore one company while siblings trade; no cursor/outbox sharing is observable.

## Tests Required

Metadata/retention, isolated restore, redrive idempotency, registration/cursor isolation.

## Edge Cases

Registry restore, old snapshot, missing snapshot, duplicate redrive, outage, retention boundary.

## Definition of Done

Operational seams, tests, and documentation evidence are complete.

## Follow-up Findings

Full disaster recovery drill belongs to Stage 31.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-014.
