# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 06e — Trading group (error correction after a premature push to `main`)
CURRENT TASK: 06e correction — audit the e7e1c87 implementation, fix every error, re-verify
CURRENT TASK STATUS: IN_PROGRESS (fixes committed: structural, domain, behavior, auth/API, tests)
NEXT READY TASK: TASK-06E-003 — Complete Stage 06e verification (needs PostgreSQL + secret store)
LAST COMPLETED TASK: Stage 06e error correction, code-complete (this session; see PROGRESS.md)
BLOCKERS: No PostgreSQL endpoint on this Windows machine (no `psql`, no Docker) — integration
  tests, migration execution and the seed fixture cannot run here. No company connection secret
  store is wired anywhere — companies cannot actually be provisioned until deployment wires one
  (pre-existing Stage 06c gap, not introduced here).
TEST STATUS:
  - `dotnet build -c Release`: 0 errors
  - Unit tests: 938 passed, 0 failed (pure-06e Domain+Application line coverage 93.3%)
  - Architecture tests: 43 passed, 0 failed (was 37 passed + 4 failed at session start)
  - Integration tests: UNVERIFIED — no PostgreSQL endpoint available on this Windows machine. Re-run required on a machine with PostgreSQL or Docker.
  - Migration `Down`: present in code for all migrations; execution against a real database UNVERIFIED due to the same PostgreSQL limitation.
IMPORTANT DECISIONS: ADR-121 – ADR-124, ADR-127 govern Stage 06e; ADR-139 records the correction
  (trigger not CHECK, registry.* permission keys, outbox link events, optional sign-in enrichment).
ENVIRONMENT LIMITATION: This session executed on Windows with no PostgreSQL binaries. Integration-test verification and migration-down execution are recorded as UNVERIFIED, not PASSED, per `AGENTS.md` rule: "UNVERIFIED is not PASS."
