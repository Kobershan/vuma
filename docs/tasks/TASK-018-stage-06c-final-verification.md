# TASK-018 — Final Stage 06c verification and documentation

## Objective
Execute the Stage 06c exit checklist, finalize documentation and hand off the next stage without starting it.
## Why
A foundation stage is complete only when behavior, migrations, tests, specialist reviews and operational docs agree.
## Scope
Stage verifier; full applicable tests/builds; migration reversibility; coverage; task/index/stage/CURRENT/PROGRESS evidence as authorized; final diff review.
## Out of scope
Stage 06d implementation, unrelated fixes, commit/push beyond the green checkpoint, and changes to business scope/locked decisions.
## Affected files/components
Stage 06c task files/index; stage document; `docs/CURRENT.md`; `docs/PROGRESS.md` only for historical evidence; changed source/test artifacts.
## Dependencies
TASK-017.
## Relevant references
`docs/EXECUTION_STANDARD.md`; `docs/AGENTS.md`; Stage 06c Exit checklist; `CLAUDE.md` §8; all ADRs named by Stage 06c.
## Implementation requirements
Run the stage verifier and applicable tests/builds; inspect diff; confirm every existing suite remains green, migrations are reversible, seed is demonstrable, and docs record unresolved limitations. Do not begin TASK-006d.
## Acceptance criteria
Exit checklist is PASS, or each UNVERIFIED item names the required environment; stage status is updated only with evidence; next handoff points to Stage 06d planning/execution as appropriate.
## Tests
Full suite; stage coverage; registry/company migration up/down; seed smoke; fan-out outage; restore/re-drive; architecture and specialist panel evidence.
## Edge cases
Environment prevents a check, stale CURRENT pointer, unrelated task becomes active, dirty diff outside Stage 06c, failed final panel.
## Security considerations
Final diff and logs contain no credentials; verify support exports/telemetry remain redacted; preserve audit evidence.
## Architectural decision impact
Confirms ADR-099 and ADR-116–120 remain unchanged; any necessary change must be recorded as a superseding ADR.
## Definition of done
All required evidence and docs are complete, status handoff is truthful, and the green checkpoint is ready for the operator.
## Status
NOT_STARTED
## Work log
- 2026-08-27: Planned as the final Stage 06c closure session; it must not start Stage 06d.

