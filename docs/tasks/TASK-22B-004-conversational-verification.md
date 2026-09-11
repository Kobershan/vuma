# TASK-22B-004 — Conversational commerce verification

**Status:** IN_PROGRESS · **Stage:** 22b · **Type:** Test / documentation / review

## Current evidence

- Unit, architecture, migration, and full PostgreSQL integration gates have passed. Regression coverage
  includes tenant-scoped replay prevention, persisted escalation, out-of-scope invoice refusal, and
  fail-closed POD behavior.

- Conversation safety unit tests: 15/15 passed.
- Stage 22b conversation migration Up/Down: 1/1 passed on real PostgreSQL.
- Full repository verification after the latest BOM fix: unit 1,353/1,353, architecture 77/77,
  integration 556/556, Release build 0 errors.

## Remaining work

- Add end-to-end tests once Stage 22 transport and the remaining module-owned document/order APIs exist.

- Add six-intent end-to-end tests, transport tests, injection fixtures, and scope-isolation tests.
- Run the conversation safety review against all ten `CHATBOT.md` reviewer checks.
- Update stage closure evidence after TASK-22B-001 through TASK-22B-003 complete.

## Definition of done

The full `CHATBOT.md` acceptance matrix is green, the specialist safety review is recorded, and
the stage is closed only after all dependencies are verified.
