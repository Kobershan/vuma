# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stages 17–31 completion pass — IN PROGRESS (2026-09-13). Numbered stages 19, 20 and 24 are complete; 12 numbered stages plus 21b, 22b and 30b remain open. Latest verified checkpoints are HR/workforce lifecycle tests, Stage 31 release-manifest verification, Stage 29 export persistence, Stage 28/27 APIs, Stage 23 service APIs, Stage 21 payment/price invariants, Stage 18 expiry refusal, and Stage 30 endpoint-bound sessions.
NEXT STAGE (roadmap order): Stage 17 closure evidence, then Stage 18 acceptance and the remaining Stage 21–31 task queues.

WORK LOG (2026-09-13): Stage 25 employee lifecycle operations are now exposed as suspend, activate
and terminate commands with HR manage-protected routes. StoreServer Release build passes with 0
errors and the HR unit suite passes 9/9. Disciplinary workflows, payroll export, roster/availability
and labour-cost integration remain open.

WORK LOG (2026-09-13): Stage 26 now exposes a `workforce.view`-protected employee availability
query, refusing invalid windows and excluding overlapping non-cancelled shifts or inactive employees.
The HR unit suite passes 11/11; roster publication, swaps and labour-cost integration remain open.

WORK LOG (2026-09-13): Stage 28 now accepts company-scoped project cost allocations with
source-reference idempotency and changed-replay conflict refusal. Project tests pass 5/5; Finance,
labour/procurement adapters and job-cost reporting remain open.

WORK LOG (2026-09-13): Stage 22 now has validated campaign scheduling and append-only outbound
message state with suppression and per-recipient idempotency metadata. Marketing tests pass 6/6;
durable transport integration and APIs remain open.

WORK LOG (2026-09-13): Marketing WhatsApp now maps to a dedicated explicit CRM consent purpose;
policy tests verify the consent check immediately before delivery. Campaign and outbound-message
state now has tenant/company-scoped EF mappings and migration `20260913221156_Stage22MarketingPersistence`;
durable delivery processing, provider transports, callbacks and APIs remain open.

WORK LOG (2026-09-13): Stage 26 now includes a validated shift-swap request state machine with
tenant-scoped EF persistence, workforce manage-protected request/decision routes, pending/
approved/rejected transitions, same-employee refusal, and handler ownership validation. HR lifecycle
tests pass 10/10 and StoreServer Release builds with 0 errors; applying approved swaps, conflict
rechecking, roster publication and labour-cost integration remain open.

STAGE 15: TASK-15-01 DONE — demand history rollup (weekly Mon–Sun, idempotent, gap zeros) + forecast engine (moving-average, exponential-smoothing, seasonal-naive behind IForecastEngine; MAPE/bias back-test; low-confidence flags) + weekly forecast run + read endpoints. TASK-15-02 DONE — safety stock (variance + 8-week fallback, horizon refusal), reorder via SafetyStockCalculation rows, ABC/XYZ snapshot runs, open-to-buy budgets with live commitments (warning only). TASK-15-03 DONE — replenishment run (ROP + forecast top-up, transfer-surplus preferred with SharedSourcing link checked at generation, 14-day expiry, backorder reattempt hook), accept/amend-accept (exactly-once)/reject through Stage 12 requisition + Stage 08 transfer commands. TASK-15-04 DONE — markdown plans (Draft→PendingApproval→Approved→Active, versioning amendments, cancel retires promotions) through IApprovalService + Stage 10 percentage-off promotions; activation sweep for missed dates. Domain (10 files) / Application (ports, 4 engines, 14 commands, 7 queries, 9 permissions, manifest, 4 hosted services) / Infrastructure (8 repos, EF configs, writers, DI) / Contracts / Web (19 endpoints) all new. Migration `20260909131616_Stage15_Planning` (9 tables, reversible, model/snapshot agree). ADR-149. Core projects (Domain/Application/Infrastructure/Contracts) build 0 errors.

CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: TASK-001 — Stage 09 review panel closure with runtime-limited evidence (2026-09-12).

TEST STATUS (Stage 15 — VERIFIED, coverage environment-limited):
  - `dotnet build VumaRetail.sln --no-restore -c Release`: PASSED — 0 errors
  - Unit tests: 1,340/1,340 passed, including 9 Planning core tests
  - Architecture tests: 77/77 passed
  - Integration: Stage 15 migration Up/Down 1/1 passed against the local PostgreSQL harness
  - Integration: Stage 16 migration Up/Down 1/1 passed against the local PostgreSQL harness
  - BOM explosion/costing unit tests: 4/4 passed
  - Manufacturing focused unit tests (BOM lifecycle, routing, explosion/costing and production execution): 20/20 passed
  - Manufacturing command handler tests: 3/3 passed
  - StoreServer build after manufacturing API wiring: PASSED — 0 errors
  - Canonical full suite: 1,340 unit, 77 architecture, and 552 integration tests passed
  - Manufacturing focused integration suite: 9/9 passed against PostgreSQL, including authorized execution,
    issue replay, genealogy and capacity readback
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

WORK LOG (2026-09-13): Storefront checkout expiry was hardened at the domain decision boundary.
`CheckoutIntent.Confirm` and `.Reject` now transition an overdue pending intent to `Expired` and
refuse the decision even when the background expiry sweep has not run. Ecommerce domain regression
tests pass 3/3. The full Release unit suite passes 1,455/1,455 after aligning the quality shortfall
assertion with its typed rule exception. Changes pushed as `21f8b73` and `3fa9049`; the latest
GitHub CI run is verifying the checkpoint.

WORK LOG (2026-09-13): Stage 23-P01 service-management foundation started. Added ticket lifecycle,
immutable warranty sale/serial snapshot, and append-only customer custody domain records; focused
tests pass 5/5. Added five `service` schema tables with reversible migration
`20260913163837_Stage23_ServiceManagement`; PostgreSQL migration Up/Down passes 1/1. APIs,
parts/accounting, SLA worker, and full isolation acceptance remain open.

Stage 23 application seams now include an EF service repository, idempotent ticket-opening,
company-scoped warranty approval and repair completion handlers, registered in StoreServer and
CloudApi. Service command/domain tests pass 23/23; StoreServer Release build passes with 0 errors.
The replication architecture gate also passes 85/85 after explicitly declaring the SLA definition
as CloudToStore/CloudWins. Latest fix is queued in GitHub CI.

Stage 23 service routes are now mapped in both hosts for tickets, warranties, and repairs, with
granular service permissions. Real-host OpenAPI plus service migration verification passes 2/2.
Ticket close is now a company-scoped command/route; the real-host OpenAPI check remains green after
the addition.
Scoped ticket and custody list queries/GET routes are now available under `service`, with repository
company/customer filters. Custody migration Up/Down plus service OpenAPI verification passes 2/2.
Service part issue now uses a dedicated inventory movement/reference and reservation-backed availability;
the command is operation-idempotent and the service-part route is included in the OpenAPI contract.
Stage 27 asset foundation is now present: fixed-asset lifecycle, company ownership, asset books, and
residual-floor straight-line depreciation are covered by a focused test (1/1); persistence and Finance
integration remain open.

WORK LOG (2026-09-13): HR/workforce lifecycle coverage added in `HrLifecycleTests` (4/4), alongside
the existing HR architecture rules (6/6). Canonical employee-core task documentation was added;
documents, disciplinary, payroll export, roster/availability and labour-cost integration remain open.
Pushed as `0304de8`.

WORK LOG (2026-09-13): Stage 31 now has `scripts/verify-release-manifest.sh`, a fail-closed SHA-256
manifest verifier with a tamper regression self-test. The self-test passes and the change was pushed
as `7dbdd94`; GitHub CI run `34778882152` is in progress.
