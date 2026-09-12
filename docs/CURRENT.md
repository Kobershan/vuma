# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 16 — BOM Setup — COMPLETE (2026-09-11). Domain lifecycle, EF persistence, graph loading/cost behavior, API routes, seed, and migration verification are complete.
NEXT STAGE (roadmap order): Stage 17 — Manufacturing execution — architecture/task specification is required before implementation.

STAGE 15: TASK-15-01 DONE — demand history rollup (weekly Mon–Sun, idempotent, gap zeros) + forecast engine (moving-average, exponential-smoothing, seasonal-naive behind IForecastEngine; MAPE/bias back-test; low-confidence flags) + weekly forecast run + read endpoints. TASK-15-02 DONE — safety stock (variance + 8-week fallback, horizon refusal), reorder via SafetyStockCalculation rows, ABC/XYZ snapshot runs, open-to-buy budgets with live commitments (warning only). TASK-15-03 DONE — replenishment run (ROP + forecast top-up, transfer-surplus preferred with SharedSourcing link checked at generation, 14-day expiry, backorder reattempt hook), accept/amend-accept (exactly-once)/reject through Stage 12 requisition + Stage 08 transfer commands. TASK-15-04 DONE — markdown plans (Draft→PendingApproval→Approved→Active, versioning amendments, cancel retires promotions) through IApprovalService + Stage 10 percentage-off promotions; activation sweep for missed dates. Domain (10 files) / Application (ports, 4 engines, 14 commands, 7 queries, 9 permissions, manifest, 4 hosted services) / Infrastructure (8 repos, EF configs, writers, DI) / Contracts / Web (19 endpoints) all new. Migration `20260909131616_Stage15_Planning` (9 tables, reversible, model/snapshot agree). ADR-149. Core projects (Domain/Application/Infrastructure/Contracts) build 0 errors.

CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: TASK-16-01 — BOM domain lifecycle, EF persistence, and migration Up/Down evidence (2026-09-10).

TEST STATUS (Stage 15 — VERIFIED, coverage environment-limited):
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
  - Focused planning unit coverage collection: BLOCKED — the existing XPlat collector hung twice without producing a report; no percentage is claimed

BLOCKERS: None for Stage 15 implementation or migration verification. Stage 15 closure evidence includes the planning unit suite (16/16), real PostgreSQL migration Up/Down (1/1), and the idempotent DemoSeed fixture for demand history, forecast, replenishment parameters, safety stock and open-to-buy. Coverage remains explicitly unmeasured because the existing XPlat collector hangs in this environment; this is recorded as an environment limitation, not represented by a fabricated percentage. Stage 21b still has supplier portal/API permission-surface work open.
ENVIRONMENT LIMITATION: None active.

DELIVERY GATE (2026-09-12): The GitHub CI workflow's Windows package job was repaired in
`bc340aa`/`54b460f`/`9d3fcc2`/`e2084c9`/`0706112` (Visual Studio 17 2022 compatibility, dedicated
vulnerability scan, PostgreSQL 17 client PATH, and pinned Windows 2022 packaging image). GitHub
run `34717343064` is GREEN, including signed Android APK and Windows Flutter packaging after the
required Android signing secrets were provisioned in GitHub. Every subsequent stage handoff must
record its commit, push, and green workflow run here.

STAGE 09 REVIEW (2026-09-12): TASK-001 is COMPLETE with an explicit environment-limited review
record. The architecture specialist brief is absent and no specialist-agent runtime is exposed;
licence-safety and sync-and-offline were checked inline against their briefs, with POS unit evidence
145/145. No new in-scope actionable finding was identified. See the task log for the limitation.

SIDE-QUEST (2026-09-10, operator-tasked, committed): Stage 19 CRM and Stage 20 Loyalty/Public API are COMPLETE after real PostgreSQL handler/API/migration verification and combined coverage evidence. Stage 22b is IN PROGRESS: identity/consent/state, explicit persisted account/company scopes, deterministic classification, signed delivery, webhook boundary, and conversation migrations are implemented and tested; six module-backed intent handlers and Stage 22 transport integration remain.

SIDE-QUEST (2026-09-11): Stage 06c implementation and PostgreSQL acceptance are complete. Focused multi-company verification and the full integration suite (568/568) pass against the disposable PostgreSQL cluster; architecture and API evidence are green. The three-company production seed script remains a deployment rehearsal, not an implementation gap.

SIDE-QUEST (2026-09-11): Stage 14b implementation and verification are complete. Field Sales approval integration is 9/9 on real PostgreSQL and the full integration suite is 568/568; API, replay, credit, sourcing, performance, rejection, and territory scenarios pass.

SIDE-QUEST (2026-09-10): Stage 13b’s implementation tasks and migration are covered by the current suite; its stale stage header is now reconciled to COMPLETE.

SIDE-QUEST (2026-09-11): Stage 07c is complete. Leg dispatch, clearing/allocation links, retry behavior, and reconciliation pass the focused PostgreSQL suite and the full 568-test integration suite; ADRs 151–152 document the remaining outbound-payment deferral.
