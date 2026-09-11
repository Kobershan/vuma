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
  - Manufacturing command handler tests: 3/3 passed
  - StoreServer build after manufacturing API wiring: PASSED — 0 errors
  - Canonical full suite: 1,340 unit, 77 architecture, and 552 integration tests passed
  - Migration `Down`: VERIFIED on real PostgreSQL; all 9 planning tables were removed
  - Coverage: NOT MEASURED (needs ≥80% on new Domain+Application)

BLOCKERS: None for compilation. Follow-ups before Stage 15 is DONE: broader Planning integration scenarios, coverage measurement, seed scenarios in DemoSeed, specialist review panel (money-and-tax, stock-availability-guard, architecture-guard, stage-verifier).
ENVIRONMENT LIMITATION: None active.

SIDE-QUEST (2026-09-10, operator-tasked, committed): Stage 19 CRM and Stage 20 Loyalty/Public API are COMPLETE after real PostgreSQL handler/API/migration verification and combined coverage evidence. Stage 22b is IN PROGRESS: identity/consent/state, explicit persisted account/company scopes, deterministic classification, signed delivery, webhook boundary, and conversation migrations are implemented and tested; six module-backed intent handlers and Stage 22 transport integration remain.

SIDE-QUEST (2026-09-11): Stage 06c implementation and PostgreSQL acceptance are complete. Focused multi-company verification and the full integration suite (568/568) pass against the disposable PostgreSQL cluster; architecture and API evidence are green. The three-company production seed script remains a deployment rehearsal, not an implementation gap.

SIDE-QUEST (2026-09-11): Stage 14b implementation and verification are complete. Field Sales approval integration is 9/9 on real PostgreSQL and the full integration suite is 568/568; API, replay, credit, sourcing, performance, rejection, and territory scenarios pass.

SIDE-QUEST (2026-09-10): Stage 13b’s implementation tasks and migration are covered by the current suite; its stale stage header is now reconciled to COMPLETE.

SIDE-QUEST (2026-09-11): Stage 07c is complete. Leg dispatch, clearing/allocation links, retry behavior, and reconciliation pass the focused PostgreSQL suite and the full 568-test integration suite; ADRs 151–152 document the remaining outbound-payment deferral.
