# TASK-011 — Company deactivation and read-only behavior

## Objective
Make deactivated companies read-only and exclude them from new business operations without deleting history.
## Why
Deactivation must stop trading safely while preserving audit, reporting, export and restoration paths.
## Scope
Deactivate command/state transition; request/operation guard; allowed reads/reprints/exports; active-company filtering and audit.
## Out of scope
Physical removal/export workflow, licensing enforcement redesign, company links, and migration serving guard.
## Affected files/components
Registry lifecycle; application authorization/guards; API error contract if needed; unit/integration tests.
## Dependencies
TASK-010; ADR-118 and `CLAUDE.md` R9/R11.
## Relevant references
Stage 06c business rules; `docs/MULTI_COMPANY.md` §9; `docs/EXECUTION_STANDARD.md`.
## Implementation requirements
Require `company.manage`; transition explicitly and audit actor/reason; refuse writes with a named error; retain reads and permitted exports; prevent deactivated companies entering new fan-outs or provisioning-dependent work.
## Acceptance criteria
Deactivation is idempotent and auditable; sales/provisioning/mutations fail; read/reprint/export paths continue where policy permits; reactivation is not accidentally implied.
## Tests
Transition tests; command guard tests; API authorization tests; allowed-read regression tests; concurrent deactivate/write test.
## Edge cases
In-flight saga, open till, pending sync, repeated deactivate, unknown company, deactivation during request.
## Security considerations
Separate management permission from business access; fail closed on writes; avoid exposing sensitive lifecycle metadata.
## Architectural decision impact
Implements ADR-118's read-only deactivation; document any reactivation/removal decision separately.
## Definition of done
Guarded behavior, tests, audit evidence, review and task handoff are green.
## Status
COMPLETE
## Work log
- 2026-08-27: Planned as a focused lifecycle policy slice.
