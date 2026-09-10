# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 15 — Merchandise Planning, Forecasting & Replenishment — CODE-COMPLETE, UNTESTED (2026-09-09). All 4 tasks implemented in one pass per operator instruction (speed over ceremony); tests, seed and verification deferred to the operator.
NEXT STAGE (roadmap order): Stage 16 (BOM Setup) — not started.

STAGE 15: TASK-15-01 DONE — demand history rollup (weekly Mon–Sun, idempotent, gap zeros) + forecast engine (moving-average, exponential-smoothing, seasonal-naive behind IForecastEngine; MAPE/bias back-test; low-confidence flags) + weekly forecast run + read endpoints. TASK-15-02 DONE — safety stock (variance + 8-week fallback, horizon refusal), reorder via SafetyStockCalculation rows, ABC/XYZ snapshot runs, open-to-buy budgets with live commitments (warning only). TASK-15-03 DONE — replenishment run (ROP + forecast top-up, transfer-surplus preferred with SharedSourcing link checked at generation, 14-day expiry, backorder reattempt hook), accept/amend-accept (exactly-once)/reject through Stage 12 requisition + Stage 08 transfer commands. TASK-15-04 DONE — markdown plans (Draft→PendingApproval→Approved→Active, versioning amendments, cancel retires promotions) through IApprovalService + Stage 10 percentage-off promotions; activation sweep for missed dates. Domain (10 files) / Application (ports, 4 engines, 14 commands, 7 queries, 9 permissions, manifest, 4 hosted services) / Infrastructure (8 repos, EF configs, writers, DI) / Contracts / Web (19 endpoints) all new. Migration `20260909131616_Stage15_Planning` (9 tables, reversible, model/snapshot agree). ADR-149. Core projects (Domain/Application/Infrastructure/Contracts) build 0 errors.

CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: Stage 15 full implementation (2026-09-09) — code-complete, untested.

TEST STATUS (Stage 15 — NOT RUN, operator to test later):
  - `dotnet build` Domain/Application/Infrastructure/Contracts: PASSED — 0 errors
  - `dotnet build VumaRetail.sln`: RED — 50 pre-existing Warehouse endpoint↔contract errors (Stage 13/13b follow-up, on main before Stage 15; untouched)
  - Unit tests: NOT RUN — no Planning tests written yet (required before DONE)
  - Architecture tests: NOT RUN
  - Integration tests: NOT RUN
  - Migration `Down`: NOT TESTED on a database
  - Coverage: NOT MEASURED (needs ≥80% on new Domain+Application)

BLOCKERS: None for compilation. Follow-ups before Stage 15 is DONE: Planning unit+integration tests, migration Up/Down on real PG, seed scenarios in DemoSeed, specialist review panel (money-and-tax, stock-availability-guard, architecture-guard, stage-verifier).
ENVIRONMENT LIMITATION: None active.

SIDE-QUEST (2026-09-10, operator-tasked, committed): Stage 19/20 stage docs written (`docs/stages/STAGE-19-crm.md`, `docs/stages/STAGE-20-loyalty-public-api.md`, NOT_STARTED; numbering corrected per ROADMAP — brief asked for STAGE-10-crm, see ADR-150); `path/to/` scaffold duplicates deleted; Domain/Crm+Loyalty scaffolding + 44 unit tests, suite 1249/1249 green. Stage position unchanged: still Stage 16 next.
