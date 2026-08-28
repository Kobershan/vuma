# Task

## Status

NOT_STARTED

## Stage

Stage 06c — Multi-company foundation

## Type

STAGE-CLOSURE, DOCUMENTATION, REVIEW

## Objective

Execute the Stage 06c exit checklist and produce a truthful handoff.

## Why

Stage completion requires agreement between implementation, migrations, tests, reviews, and docs.

## Scope

Stage verifier, final applicable tests/builds, migration reversibility, coverage, stage/index/current/progress evidence, and final diff review.

## Out of Scope

Stage 06d implementation, unrelated fixes, and changing scope or locked decisions.

## Architecture

Verify the complete Stage 06c blueprint: registry → routing/context → isolated company DBs → fan-out/sagas → API → backup/sync. Closure owns evidence only.

## Architectural Boundaries

Do not mark complete with unverified required checks. Confirm application changes are limited to authorized implementation tasks; no cross-database transaction exists.

## Dependencies

TASK-06C-13.

## Relevant Files

Stage 06c docs, canonical task files, `docs/CURRENT.md`, `docs/PROGRESS.md`, changed source/test diff.

## Relevant Documentation

`docs/EXECUTION_STANDARD.md`, `docs/AGENTS.md`, Stage 06c exit checklist, all referenced ADRs.

## Implementation Requirements

Run verifier and applicable checks; record PASS/UNVERIFIED honestly; update handoff to Stage 06d only after evidence.

## Data/Database Impact

Verify per-company migration up/down, seed, restore, and registry state.

## API Impact

Verify OpenAPI and endpoint evidence.

## Security

Verify logs/diffs contain no credentials and support/telemetry restrictions remain intact.

## Multi-Company/Tenant Impact

Verify three-company isolation and lifecycle behavior.

## Sync/Offline Impact

Verify independent cursors, outboxes, restore, and redrive.

## Acceptance Criteria

Exit checklist passes or each unverified item names its required environment; status handoff is truthful.

## Tests Required

Full suite, coverage, migration, seed, fan-out outage, restore/redrive, architecture, and specialist evidence.

## Edge Cases

Environment limitation, stale pointer, unrelated diff, failed final panel.

## Definition of Done

All evidence and documentation are complete; no next stage is started.

## Follow-up Findings

Record Stage 06d planning needs separately.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-018.
