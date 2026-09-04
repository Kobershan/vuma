# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 06d — Group services (credit groups, barcode routing index, saga coordinator, group read models)
CURRENT TASK: TASK-06D-001 — Implement saga coordination
CURRENT TASK STATUS: NOT_STARTED
NEXT READY TASK: TASK-06D-001 — Implement saga coordination
LAST COMPLETED TASK: Stage 06c closure + dependency fix (SSH.NET pin, branch rebase)
BLOCKERS: None. Stage 06c build is green.
TEST STATUS:
  - `dotnet build -c Release`: 0 errors, 0 warnings in Domain/Application
  - Unit tests: 895 passed, 0 failed
  - Architecture tests: 41 passed (including new MultiCompanyGuardTests), 0 failed
  - Integration tests: UNVERIFIED — no PostgreSQL endpoint available on this Windows machine (no `psql`, no Docker, no `scripts/pg-test.ps1`). Re-run required on a machine with PostgreSQL or Docker.
  - Migration `Down`: present in code for all migrations; execution against a real database UNVERIFIED due to the same PostgreSQL limitation.
IMPORTANT DECISIONS: ADR-099, ADR-116, ADR-117, ADR-118, ADR-119, and ADR-120 govern Stage 06c.
ENVIRONMENT LIMITATION: This session executed on Windows with no PostgreSQL binaries. Integration-test verification and migration-down execution are recorded as UNVERIFIED, not PASSED, per `AGENTS.md` rule: "UNVERIFIED is not PASS."
