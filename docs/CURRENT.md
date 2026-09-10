# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 16 — BOM Setup — IN PROGRESS (2026-09-10). Domain lifecycle, EF persistence, pure explosion/costing, and initial API routes are implemented; graph-loading/cost API behavior, seed, and stage exit verification remain open.
NEXT STAGE (roadmap order): Stage 16 follow-up layers — graph-loading/cost API behavior, seed, and exit verification.

STAGE 15: TASK-15-01 DONE — demand history rollup (weekly Mon–Sun, idempotent, gap zeros) + forecast engine (moving-average, exponential-smoothing, seasonal-naive behind IForecastEngine; MAPE/bias back-test; low-confidence flags) + weekly forecast run + read endpoints. TASK-15-02 DONE — safety stock (variance + 8-week fallback, horizon refusal), reorder via SafetyStockCalculation rows, ABC/XYZ snapshot runs, open-to-buy budgets with live commitments (warning only). TASK-15-03 DONE — replenishment run (ROP + forecast top-up, transfer-surplus preferred with SharedSourcing link checked at generation, 14-day expiry, backorder reattempt hook), accept/amend-accept (exactly-once)/reject through Stage 12 requisition + Stage 08 transfer commands. TASK-15-04 DONE — markdown plans (Draft→PendingApproval→Approved→Active, versioning amendments, cancel retires promotions) through IApprovalService + Stage 10 percentage-off promotions; activation sweep for missed dates. Domain (10 files) / Application (ports, 4 engines, 14 commands, 7 queries, 9 permissions, manifest, 4 hosted services) / Infrastructure (8 repos, EF configs, writers, DI) / Contracts / Web (19 endpoints) all new. Migration `20260909131616_Stage15_Planning` (9 tables, reversible, model/snapshot agree). ADR-149. Core projects (Domain/Application/Infrastructure/Contracts) build 0 errors.

CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: TASK-16-01 — BOM domain lifecycle, EF persistence, and migration Up/Down evidence (2026-09-10).

TEST STATUS (Stage 15 — PARTIALLY VERIFIED):
  - `dotnet build VumaRetail.sln --no-restore -c Release`: PASSED — 0 errors
  - Unit tests: 1,340/1,340 passed, including 9 Planning core tests
  - Architecture tests: 77/77 passed
  - Integration: Stage 15 migration Up/Down 1/1 passed against the local PostgreSQL harness
  - Integration: Stage 16 migration Up/Down 1/1 passed against the local PostgreSQL harness
  - BOM explosion/costing unit tests: 4/4 passed
  - Manufacturing focused unit tests (BOM lifecycle, routing, explosion/costing): 10/10 passed
  - StoreServer build after manufacturing API wiring: PASSED — 0 errors
  - Canonical full suite: 1,340 unit, 77 architecture, and 552 integration tests passed
  - Migration `Down`: VERIFIED on real PostgreSQL; all 9 planning tables were removed
  - Coverage: NOT MEASURED (needs ≥80% on new Domain+Application)

BLOCKERS: None for compilation. Follow-ups before Stage 15 is DONE: broader Planning integration scenarios, coverage measurement, seed scenarios in DemoSeed, specialist review panel (money-and-tax, stock-availability-guard, architecture-guard, stage-verifier).
ENVIRONMENT LIMITATION: None active.

SIDE-QUEST (2026-09-10, operator-tasked, committed): Stage 19/20 stage docs written (`docs/stages/STAGE-19-crm.md`, `docs/stages/STAGE-20-loyalty-public-api.md`, NOT_STARTED; numbering corrected per ROADMAP — brief asked for STAGE-10-crm, see ADR-150); `path/to/` scaffold duplicates deleted; Domain/Crm+Loyalty scaffolding + 44 unit tests, suite 1249/1249 green. Stage position unchanged: still Stage 16 next.

SIDE-QUEST (2026-09-10, operator-tasked: "07c to 14"): TASK-07C-004 COMPLETE — leg-dispatch rework proven on real PG (unit 1249/1249, `GroupReceiptLegsTests` 8/8, arch 76/77 with one pre-existing wall-clock failure in Stage 19/20 scaffolding). ADRs 151 (clearing→allocation link) + 152 (outbound group payments deferred). Stage 07c needs `stage-verifier` + exit checklist before DONE; next in operator scope is Stage 08c.
