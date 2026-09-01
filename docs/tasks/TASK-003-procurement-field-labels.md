# Task TASK-003 — Correct Procurement matched-value field labels

## Objective

Correct the Procurement `MatchedNet`/`PriceVariance` naming identified in `PROGRESS.md` §4.20, if
the persisted/domain meaning confirms the fields represent gross values.

## Why

Misleading financial names can cause incorrect downstream interpretation even when arithmetic is
currently correct.

## Scope

Trace the fields, rename only the affected domain/DTO/database surfaces, update migrations if the
names are persisted, and update tests and documentation.

## Out of scope

Changes to matching arithmetic, tax rules, posting rules, or unrelated Procurement behavior.

## Files/components likely affected

Procurement domain/application/infrastructure/API files, migration if required, and focused tests.

## Dependencies

TASK-001 and the Procurement review findings in `PROGRESS.md` §4.20.

## Relevant references

`docs/stages/STAGE-12-procurement.md`, `docs/DECISIONS.md` ADR-082–ADR-086,
`docs/PROGRESS.md` §4.20, `docs/DATA_MODEL.md`.

## Implementation requirements

Confirm semantic meaning before changing names. Preserve financial precision, audit history,
backward compatibility requirements, and migration reversibility.

## Acceptance criteria

The names accurately describe the values, all consumers compile, persisted changes are reversible,
and focused financial tests pass.

## Tests required

Procurement unit/integration tests, migration up/down if schema changes, and relevant API tests.

## Edge cases

Tax-inclusive versus tax-exclusive values, zero variance, negative variance, and historical rows.

## Security considerations

Do not expose additional supplier or invoice data through renamed API fields.

## Architectural decision impact

Record an ADR only if the correction changes a durable financial interpretation.

## Definition of done

Names, consumers, tests, migrations, documentation, and task state are all reconciled and reviewed.

## Status

NOT_STARTED
