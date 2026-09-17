# TASK-22B-004 — Conversational commerce verification

**Status:** IN_PROGRESS · **Stage:** 22b · **Type:** Test / documentation / review

## Current evidence

- Unit, architecture, migration, and full PostgreSQL integration gates have passed. Regression coverage
  includes tenant-scoped replay prevention, persisted escalation, out-of-scope invoice refusal, and
  fail-closed POD behavior.

- Conversation safety unit tests: 15/15 passed.
- Stage 22b conversation migration Up/Down: 1/1 passed on real PostgreSQL.
- 2026-09-17: Conversation safety regression suite passes **25/25**, including deterministic
  injection handling and fabricated number/date rejection. Signed webhook, normalized email,
  transcript isolation and transport audit API tests pass **5/5**; delivery-audit migration
  up/down/up passes on PostgreSQL.
- Full repository verification after the latest BOM fix: unit 1,353/1,353, architecture 77/77,
  integration 556/556, Release build 0 errors.

## Remaining work

- Add provider-backed end-to-end tests once Stage 22 provider deployment and remaining module-owned
  document/order APIs exist.

- Add six-intent provider-backed end-to-end tests and run the specialist safety review against all
  ten `CHATBOT.md` reviewer checks. Repository-level injection, transport, and scope tests are now
  present; TASK-22B-001 through TASK-22B-003 are complete.

## Definition of done

The full `CHATBOT.md` acceptance matrix is green, the specialist safety review is recorded, and
the stage is closed only after all dependencies are verified.
