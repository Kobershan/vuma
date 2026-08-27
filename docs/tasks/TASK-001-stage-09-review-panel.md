# Task TASK-001 — Run the independent review panel for Stage 09 closure

## Objective

Run the outstanding independent specialist reviews for the Stage 09 closure commits and record the
evidence before new implementation work begins.

## Why

The closure fixes were reviewed directly after the previous panel hit an account usage limit. The
repository requires independent review evidence; direct author review is not equivalent.

## Scope

- Review `39e8783`, `6b014ce`, and `86f8dbd`.
- Run `architecture-guard`, `licence-safety`, and `sync-and-offline`.
- Fix only defects found in this closure, or document each accepted/unverified finding.
- Update this task, `docs/CURRENT.md`, and the historical state in `docs/PROGRESS.md`.

## Out of scope

- OpenAPI example sweeps, Procurement naming, Stage 11 coverage, Stage 05 implementation, or any
  unrelated refactor.
- Reopening locked ADRs 135–138.

## Files/components likely affected

`src/VumaRetail.Licensing/`, `src/VumaRetail.Application/Pos/`,
`src/VumaRetail.Web/Pos/`, related POS tests, and workflow documentation only if findings require it.

## Dependencies

Stage 09 closure commits are already on `main`. The review agents and their briefs in `.claude/agents/`
are read-only.

## Relevant references

`docs/stages/STAGE-09-pos-terminal.md`, `docs/DECISIONS.md` ADR-135–ADR-138,
`docs/PROGRESS.md` §4.10–§4.15 and §5, `docs/AGENTS.md`.

## Implementation requirements

Verify by execution where the environment permits. Treat unavailable Windows, Docker, or agent
runtime checks as explicitly unverified. Preserve the existing POS offline and read-only rules.

## Acceptance criteria

- All three specialist reviews have a report or an explicit environment failure recorded.
- Every actionable finding is fixed or has a written reason and follow-up.
- Relevant tests are rerun after fixes.
- No unrelated files or defects are included.

## Tests required

Run the affected POS unit/integration tests, architecture tests where boundaries change, and the
full applicable suite after any fix. Record exact commands and counts.

## Edge cases

- Replay must remain idempotent and must not mint duplicate outbox events.
- Read-only exemptions must remain bounded by the deadlines in ADR-135.
- Reprint authorization must distinguish a reprint from a first print.

## Security considerations

Do not weaken entitlement enforcement, permission checks, terminal/session scoping, or auditability.

## Architectural decision impact

No new decision is expected. Any change to ADR-135–ADR-138 requires a superseding ADR.

## Definition of done

- Review evidence recorded.
- Findings resolved or explicitly documented.
- Relevant validation is green, or limitations are marked `UNVERIFIED`.
- Task and current-state handoff updated.
- Diff reviewed and limited to this task.

## Status

IN_PROGRESS

## Work log

- Created as the first task in the task-sized workflow. The previous session could not run the
  independent panel because of an account usage limit.
