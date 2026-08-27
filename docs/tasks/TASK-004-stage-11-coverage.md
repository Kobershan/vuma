# Task TASK-004 — Raise Stage 11 Domain/Application coverage to the required floor

## Objective

Bring Stage 11's measured Domain and Application line coverage from the documented 76.80% to the
repository-required 80% floor through meaningful missing-behavior tests.

## Why

The stage exit requirement is not met even though the existing suite is green.

## Scope

Identify uncovered Stage 11 behavior, especially rollback and validation edges, and add focused
tests that prove real behavior.

## Out of scope

Production refactors, trivial coverage padding, OCR implementation, or unrelated test cleanup.

## Files/components likely affected

`src/VumaRetail.Imports/`, relevant Application import code, and Stage 11 unit/integration tests.

## Dependencies

TASK-001 and TASK-002 may be completed first; no code dependency is expected.

## Relevant references

`docs/stages/STAGE-11-data-import.md`, `docs/IMPORT_PIPELINE.md`, `docs/TESTING.md`,
`docs/PROGRESS.md` §4.24.

## Implementation requirements

Tests must cover meaningful branches and preserve import idempotency, validation, rollback, and
failure semantics. Do not alter production behavior solely to make coverage easier.

## Acceptance criteria

- Measured Stage 11 Domain/Application coverage is at least 80%.
- New tests fail if the covered behavior regresses.
- Full relevant test suite remains green.

## Tests required

Stage 11 unit and integration tests with the exact coverage command and measured result recorded.

## Edge cases

Invalid mappings, duplicate rows, rollback retries, malformed input, partial failures, and PDF
documents without a text layer.

## Security considerations

Use synthetic import data and ensure diagnostics do not leak imported credentials or personal data.

## Architectural decision impact

None expected.

## Definition of done

Coverage is measured, behavior is meaningfully tested, the diff is reviewed, and task/current state
are updated.

## Status

NOT_STARTED
