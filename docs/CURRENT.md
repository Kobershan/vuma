# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stages 18–31 completion pass — IN PROGRESS (2026-09-14). Stage 17 is complete with
API, PostgreSQL, seed, encrypted-backup, replay, genealogy, capacity and documented specialist-runtime
limitation evidence. Remaining work is tracked in the open stage/task queues.
NEXT STAGE (roadmap order): Complete Stage 18 PostgreSQL dispatch and recall-traceability acceptance,
then Stage 21 checkout/payment acceptance and Stage 21b.

WORK LOG (2026-09-14): Stage 21 now has an explicit replay-safe payment operation boundary for
capture, void and refund. Operations validate the active company, checkout, current provider state
and idempotency fingerprint before invoking the gateway, then append the resulting payment attempt.
Ecommerce tests pass **11/11**; full unit and architecture suites remain green at **1,583/1,583** and
**85/85**. Authoritative order/reservation orchestration and PostgreSQL payment integration remain.

WORK LOG (2026-09-14): Exposed the capture/void/refund boundary through the payment-protected
`/api/v1/storefront/checkouts/{id}/payment/{operation}` route. Invalid operation names fail with 400;
valid operations retain the command's state and replay checks. Route OpenAPI verification remains
part of the pending Stage 21 PostgreSQL acceptance.

WORK LOG (2026-09-14): Stage 18 dispatch authorization now checks the existing quality-hold boundary
and the inventory ledger's net expired tracked stock at the supplied business date before any bin move
or shipment issue. Added `QUALITY_DISPATCH_EXPIRED_STOCK`, repository query coverage and a focused
regression test. Receipt commands now carry lot, expiry and serial metadata, and the real PostgreSQL
warehouse chain proves the expiry refusal. Application and Infrastructure Release builds compile;
focused quality/warehouse unit tests pass **89/89**. Automatic lot-to-output/shipment recall-
traceability acceptance remains open.

STAGES 00 AND 06 UPDATE (2026-09-14): TASK-00-002 is complete after GitHub Actions run
`34868258264` passed on `main`. The Stage 00 and Stage 06 architecture-map planning rows are
closed as documentation gates; TASK-06-001 was already complete. Focused Stage 00/06 tests pass
(92 unit tests and 85 architecture tests).

WORK LOG (2026-09-14): Repaired the Stage 22 marketing journey migration by scaffolding the EF
designer/snapshot from the Release model, and corrected the three-segment manufacturing permission
keys plus due-time marketing delivery fixtures. Release build passes with **0 errors**; unit tests
pass **1,579/1,579**, architecture tests **85/85**, focused migration/marketing/conversation tests
**8/8**, and focused manufacturing/quality/ecommerce/marketing/logistics API tests **15/15**.

VERIFICATION (2026-09-14): Release unit tests pass **1,566/1,566** and architecture tests pass
**85/85**. A focused PostgreSQL manufacturing migration run passes **2/2**. The complete
integration suite was started twice but exceeded a five-minute diagnostic limit without emitting a
failure; it is intentionally serialized and provisions an isolated database per test, so this is a
runtime limit rather than passing evidence. Do not mark the remaining stages complete from the unit
or focused migration results alone.

WORK LOG (2026-09-14): Stage 17 PostgreSQL API verification exposed and fixed a per-request company
context gap on BOM reads/publication and production mutations. Company binding is now explicit on
those routes while handlers remain fail-closed. Manufacturing API integration passes **7/7** and
manufacturing migration verification passes **2/2**; specialist closure evidence remains open.

AUDIT NOTE (2026-09-14): Stage 21 is not genuinely complete despite its closure label; authoritative
order/reservation/gateway, capture/refund, outage and PostgreSQL acceptance remain open. Stage 21b
also remains open for supplier portal end-to-end trading, ASN/GRN, isolation, settlement and offline
convergence acceptance.

AUDIT NOTE (2026-09-14): Stage 18 dispatch enforcement is now wired into warehouse shipping before
stock movement. The stage is not marked complete: source-built quality regression, PostgreSQL
shelf-life/dispatch verification, and automatic recall traceability remain open.

VERIFICATION (2026-09-14): Stage 18 quality API integration passes **4/4** against PostgreSQL,
covering OpenAPI exposure, permission denial, and reservation-backed hold idempotency/shortfall
behavior. Full quality acceptance and specialist closure evidence remain open.

WORK LOG (2026-09-14): Stage 18 quality hold release/rejection, inspection, corrective-action and
non-conformance transitions now validate loaded tenant scope alongside company scope. Quality unit
tests pass **13/13**; NCR/CAPA, recall, certificate and full acceptance remain open.

VERIFICATION (2026-09-14): The quality suite was rerun after the tenant guards compiled; **13/13**
tests passed and the Application build reports **0 errors**.

WORK LOG (2026-09-14): Stage 25 employee lifecycle, leave decisions and shift-swap decisions now
validate loaded tenant scope before state changes. HR-focused tests pass **102/102**; payroll
delivery, statutory integration, roster/availability and full acceptance remain open.

VERIFICATION (2026-09-14): Stage 26 employee/shift creation and availability now reject loaded
employees from another tenant, and HR-focused tests pass **102/102** after compilation.

VERIFICATION (2026-09-14): The full PostgreSQL integration run reached **607/608** before exposing
one stale trailing-slash expectation in the shared API contract list. The Stage 27 asset and
maintenance paths use canonical `/api/v1/assets` and `/api/v1/maintenance/orders` OpenAPI paths;
after correcting those expectations, `ApiContractTests` passes **35/35**.

VERIFICATION (2026-09-14): Stage 23 service migration Up/Down acceptance passes **1/1** against
PostgreSQL. SLA worker execution, financial integration, and specialist closure evidence remain open.

VERIFICATION (2026-09-14): Stage 22b conversation migration and conversation-scope migration
acceptance passes **3/3** against PostgreSQL. Transport delivery, remaining intent/API work, and
specialist closure evidence remain open.

VERIFICATION (2026-09-14): Stage 22 business-identity/transfer migration acceptance passes **6/6**
against PostgreSQL. Full transfer saga, ledger, discrepancy, and specialist closure evidence remain
open.

WORK LOG (2026-09-14): Stage 22 marketing outbound-message suppression and send handlers now fail
closed on the loaded tenant as well as company before changing state. `MarketingDeliveryPolicyTests`
passes **13/13**; durable transport, callbacks, attribution, and full stage acceptance remain open.

AUDIT NOTE (2026-09-14): Focused Marketing/Conversations unit tests pass **21/21**. Conversation
migration/API integration tests were attempted but cannot start without Docker/PostgreSQL. Stage 22
and 22b remain open because durable transport processing and the remaining module-backed intent
handlers are not implemented.

WORK LOG (2026-09-14): Stage 28 project cost allocation, budget approval, contract variation
approval and milestone billing now fail closed on both tenant and company scope. The changed-content
replay check also validates the persisted tenant/company boundary. Focused project tests pass **7/7**;
Finance, labour/procurement adapters and job-cost reporting remain open.

VERIFICATION (2026-09-14): Re-ran the full Release unit suite (**1,561/1,561**) and architecture suite
(**85/85**) after the Stage 17–31 security hardening pass. The three executable Stage 31 release
script tests (`activate-release`, `verify-release-manifest`, and `verify-release-signature`) pass
(**3/3**). Android assembly remains a GitHub-only check on this machine because the Gradle executable
is not installed locally; the pushed CI workflow is the authoritative Android build gate.

WORK LOG (2026-09-14): Added a dedicated `android-compose-build` GitHub job using JDK 17 and
Gradle 8.9, and made packaging depend on it. Stage 30 Android compilation is now an enforced CI
gate; its first run is pending.

WORK LOG (2026-09-14): Stage 27 checklist execution replay and evidence authorization now enforce
the loaded tenant boundary in addition to company/store scope. Asset-focused tests pass **15/15**;
period-close, external evidence storage and full stage acceptance remain open.

VERIFICATION (2026-09-14): All four local Stage 31 release-script tests pass (**4/4**), and the
full unit suite passes **1,561/1,561** after the Stage 27 tenant-scope change. GitHub workflow
completion, Android assembly, DR, packaging and specialist closure remain open.

VERIFICATION (2026-09-14): GitHub CI run `34800395226` completed green across release-manifest
verification, backend build, unit/integration tests, architecture, migration, vulnerability,
design-system, Android Compose, and Windows package jobs. The later tenant-wide metadata fix is
running in a separate authoritative workflow and still requires its own result.

VERIFICATION (2026-09-14): GitHub CI run `34802104182` completed green for commit `8f6b19f`,
including backend build, migration, unit/integration, architecture, vulnerability, design-system,
Android Compose, release-manifest and Windows package jobs. The local full PostgreSQL integration
suite also passes **608/608** after the demo company-scope fix.

WORK LOG (2026-09-14): Added deterministic release-manifest generation with exclusions for source
control, build caches, local configuration and signing material. The complete local release-script
set now passes **4/4**; packaging, DR and GitHub workflow completion remain open.

WORK LOG (2026-09-14): The durable Android action adapter now rejects empty company scopes before
querying Room, keeping offline claims fail-closed for sessions without effective company access.

WORK LOG (2026-09-14): CI now runs the manifest-generation test and cancels superseded pushes per
branch, keeping the latest release gate authoritative during long builds.

WORK LOG (2026-09-14): Clean PostgreSQL DR seeding exposed and fixed company-scope omissions for
the demo tenant/store and tenant-wide stock/catalog metadata. StoreServer builds with **0 errors**;
the clean restore drill now passes: snapshot `01a09dea-b7bc-7000-9314-ac813ac94de5` was verified,
restored into `vuma_drill_restored`, and matched `users=5 roles=4 stores=2`.

WORK LOG (2026-09-14): Stage 30 now includes Room-backed tenant/company dashboard cache and
pending-action persistence, plus an Android Keystore AES-GCM encrypted refresh-token store backed
by DataStore. Android compilation and instrumentation acceptance remain CI-dependent.

WORK LOG (2026-09-14): Extended the Kotlin API client with the `/api/v1/dashboard/overview`
contract, preserving separate currency totals and the server AsAt timestamp for cached reads.
Android compilation and endpoint/instrumentation acceptance remain CI-dependent.

WORK LOG (2026-09-14): Stage 22 marketing provider results now persist provider event identity and
payload fingerprints, reject changed-content callback replays, and expose a signed callback route.
The focused marketing run passes **16/16**; company-scoped campaign/message operator reads are now
available, while transport workers and audience APIs remain open.

WORK LOG (2026-09-14): Outbound marketing messages now persist explicit channel and message
classification metadata through migration `20260914041306_Stage22MarketingDeliveryMetadata`, so
transport selection and consent policy no longer depend on inferred defaults. Focused marketing
tests pass **17/17**. A bounded, company-scoped due-queue operator read is now available.

WORK LOG (2026-09-14): Added the consent-aware marketing dispatch application boundary. Dispatch
rechecks recipient consent immediately before transport, suppresses withdrawn recipients, and
applies provider event identity/fingerprint results to outbound state. Focused marketing tests pass
**19/19**; provider adapter and durable worker wiring remain open.

VERIFICATION (2026-09-14): Marketing API integration coverage passes **2/2**, including OpenAPI
route discovery and runtime rejection of an unsigned provider callback.

WORK LOG (2026-09-14): Wired the Android process composition root to create the Room database and
Keystore-backed session store, and documented the durable state boundary. Android compilation and
instrumentation acceptance remain CI-dependent.

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

WORK LOG (2026-09-14): Stage 23 ticket and service-part replay paths now validate the active company
before returning existing records. Service command tests pass 4/4; PostgreSQL availability and
financial acceptance remain open.

WORK LOG (2026-09-14): Stage 17 production-order creation now enforces the active company at both
the API boundary and command handler. Manufacturing command tests pass 6/6; broader execution and
specialist closure evidence remain open.

WORK LOG (2026-09-14): Stage 26 roster publications now use the selected store-filtered shift set
for both the canonical hash and shift count. HR lifecycle tests pass 12/12; distribution and
labour-cost integration remain open.

WORK LOG (2026-09-14): Stage 27 checklist submission now verifies the persisted checklist’s company
and store ownership before accepting an execution. Checklist command tests pass 5/5; evidence
transport and period-close acceptance remain open.

WORK LOG (2026-09-14): Stage 17 BOM creation now enforces the active company at the API and command
boundaries. Manufacturing command tests pass 6/6; broader execution and closure evidence remain
open.

WORK LOG (2026-09-14): Stage 17 BOM publication and retrieval now enforce the loaded definition’s
active company boundary. Manufacturing command tests pass 7/7; PostgreSQL and closure evidence
remain open.

WORK LOG (2026-09-14): Stage 17 production release, material issue, output, scrap and close
handlers now fail closed when the loaded order/BOM is outside the active company; material issue
no longer changes the ambient company from loaded data. `ManufacturingCommandTests` passes 8/8;
PostgreSQL backup/seed and specialist closure evidence remain open.

WORK LOG (2026-09-14): Stage 23 SLA creation now enforces active-company scope before duplicate
lookup or persistence. Service domain tests pass 6/6; worker escalation and full acceptance remain
open.

WORK LOG (2026-09-14): Stage 27 asset placement/disposal now validate active-company scope before
loading the asset. Asset-focused tests pass 5/5; persistence, finance posting, and period-close
acceptance remain open.

WORK LOG (2026-09-14): Stage 27 checklist evidence now has a low-risk view permission and a
company-scoped 15-minute opaque HMAC authorization grant endpoint. `ChecklistEvidenceTests` passes
2/2; external evidence storage and period-close acceptance remain open.

WORK LOG (2026-09-13): Marketing WhatsApp now maps to a dedicated explicit CRM consent purpose;
policy tests verify the consent check immediately before delivery. Campaign and outbound-message
state now has tenant/company-scoped EF mappings, repositories, company-guarded commands and
manage-protected API routes in migration `20260913221156_Stage22MarketingPersistence`; durable
delivery processing, provider transports and callbacks remain open.

WORK LOG (2026-09-13): Stage 27 checklist execution now has tenant/company-scoped persistence,
company-guarded create/submit commands and manage-protected API routes; matching operation replays
are idempotent and changed content is refused. StoreServer Release builds with 0 errors; evidence
authorization and period-close acceptance remain open.

WORK LOG (2026-09-13): Stage 27 checklist command coverage now verifies company-scoped creation,
identical operation replay idempotency and changed-evidence replay refusal (`ChecklistCommandTests`
3/3). Evidence attachment authorization and period-close acceptance remain open.

WORK LOG (2026-09-13): Stage 22 marketing command authorization now rejects idempotency keys and
message state changes crossing the active tenant/company boundary. `MarketingDeliveryPolicyTests`
passes 12/12; durable transport, callbacks and attribution remain open.

WORK LOG (2026-09-13): Stage 25 now has an auditable disciplinary-case domain state machine that
requires investigation before a one-way decision and preserves event ordering. `DisciplinaryCaseTests`
passes 2/2; HR persistence/workflow routes and payroll export remain open.

WORK LOG (2026-09-13): Stage 25 disciplinary cases are now company-scoped, persisted by migration
`Stage25DisciplinaryCases`, and exposed through HR manage-protected open/investigate/decision routes.
`DisciplinaryCaseTests` passes 3/3 and StoreServer builds with 0 errors; payroll export, case listing
and full acceptance remain open.

WORK LOG (2026-09-14): Stage 25 disciplinary investigation and decision handlers now enforce the
loaded case’s active company. Disciplinary tests pass 5/5; payroll and specialist acceptance remain
open.

WORK LOG (2026-09-13): Stage 25 disciplinary case listing now enforces the active company and is
available through the HR view-protected query/API surface. `DisciplinaryCaseTests` passes 4/4;
payroll export, acceptance and specialist review remain open.

WORK LOG (2026-09-13): Stage 25 disciplinary mutations now use the dedicated high-risk
`hr.disciplinary.manage` permission rather than the general employee-management permission;
disciplinary regression tests remain green at 4/4.

WORK LOG (2026-09-13): Stage 25 now provides a source-only payroll export query and protected API
that pairs immutable attendance sessions with contract rates, subtracts explicit breaks, and fails
closed on incomplete sessions or missing contracts. `PayrollExportTests` passes 2/2; statutory
calculation, durable file delivery and provider integration remain open.

WORK LOG (2026-09-13): Stage 25 payroll source rows are now also available through deterministic
CSV with escaping at `/api/v1/hr/payroll/export.csv`; `PayrollExportTests` passes 3/3.

WORK LOG (2026-09-13): Stage 29 report exports now have guarded completion/failure handoff commands
and a persisted artifact reference via `Stage29ReportExportArtifactsFix`; export requests require
`reporting.report.manage`. `ReportingDomainTests` passes 8/8; renderer/storage, scheduling and
expiring download authorization remain open.

WORK LOG (2026-09-14): Stage 29 export completion, failure, status and download authorization now
enforce tenant as well as company scope, and export handoff routes bind the request company before
dispatch. `ReportingDomainTests` passes **9/9**; renderer/storage and scheduling remain open.

WORK LOG (2026-09-14): Stage 22 marketing create and queue routes now bind their request company;
campaign/message state transitions accept an explicit company selector before dispatch. StoreServer
build passes with **0 errors**; durable transport, callbacks, attribution and acceptance remain open.

WORK LOG (2026-09-14): Stage 22b registry context now declares tenant filters for contact bindings,
conversation account scopes and verification challenges in addition to their entity configurations.
Both company and registry EF contexts report no pending model changes.

WORK LOG (2026-09-14): Stage 23 warranty claim approval now validates the loaded claim tenant as
well as the active company before changing state. `ServiceCommandTests` passes **4/4**; SLA worker,
financial integration and full service acceptance remain open.

WORK LOG (2026-09-14): Stage 23 repair completion and ticket resume/close now validate the loaded
record tenant as well as company scope before changing state. StoreServer build passes with **0
errors**; SLA worker, financial integration and full service acceptance remain open.

VERIFICATION (2026-09-14): Stage 23 service unit tests pass **33/33** after adding tenant scope to
SLA deadline reads and service lifecycle mutations; the StoreServer build remains at **0 errors**.

VERIFICATION (2026-09-13): The originally generated empty Stage 29 artifact migration was removed
and replaced with `Stage29ReportExportArtifactsFix`, which adds
`reporting.report_exports.artifact_reference`. Infrastructure rebuild and EF pending-model
verification are clean.

VERIFICATION (2026-09-13): The post-payroll architecture suite passes 85/85 and
`dotnet ef migrations has-pending-model-changes` reports no changes. GitHub CI for the latest
checkpoint remains in progress.

WORK LOG (2026-09-13): Stage 23 now exposes company-scoped SLA response/resolution deadlines and
breach flags through the service query/API surface using configured weekday business hours.
`ServiceSlaClockTests` passes 5/5; SLA worker scheduling and full acceptance remain open.
WORK LOG (2026-09-13): Stage 23 waiting-for-customer pause accounting is now explicit: ticket pause
state and accumulated working hours persist, resume is company-scoped at `/resume`, and SLA deadlines
exclude paused working time. `ServiceSlaClockTests` passes 6/6; breach worker and PostgreSQL acceptance
remain open.
WORK LOG (2026-09-13): Stage 21 payment notification replay now checks company ownership before
returning an idempotent result. `EcommerceDomainTests` passes 9/9; provider orchestration, outage
integration, and full checkout acceptance remain open.
The signed payment webhook also now sets the active company context before dispatch, preserving the
same company-isolation guard at the HTTP boundary; Store Web build passes with 0 errors.
WORK LOG (2026-09-13): Stage 25 employee-document listings now expose metadata-only DTOs; external
blob keys are no longer returned by the HR API. Expiring download authorization and retention remain
open.
WORK LOG (2026-09-13): Stage 25 now issues 15-minute opaque HMAC-signed document download grants
through the HR API; expired metadata is refused and the blob key is excluded from the token payload.
`EmployeeDocumentTests` passes 7/7, including token tamper and exact-expiry rejection. Storage-side
validation and retention remain open.

WORK LOG (2026-09-13): Stage 26 now includes a validated shift-swap request state machine with
tenant-scoped EF persistence, workforce manage-protected request/decision routes, pending/
approved/rejected transitions, same-employee refusal, and handler ownership validation. HR lifecycle
tests pass 11/11 and StoreServer/CloudApi builds with 0 errors; approved swaps now apply atomically
after target conflict and owner-change checks, and roster publication persists deterministic hashed
snapshots. Labour-cost integration and specialist review remain open.

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
WORK LOG (2026-09-13): Stage 29 completed-export downloads now issue 15-minute company-scoped opaque
HMAC grants; `ReportingDomainTests` passes 9/9 and the validation boundary rejects tampering/expiry.
Renderer/storage consumption, scheduled worker execution, and rebuild remain open.
WORK LOG (2026-09-13): Stage 28 now exposes a company-scoped project cost summary grouped by currency,
including reversal entries without implicit FX conversion. `ProjectCostTests` passes 3/3; adapters,
Finance posting and integration acceptance remain open.
WORK LOG (2026-09-14): Stage 27 checklist operation replay now compares store/device and capture/
submit timestamps as well as evidence. `ChecklistCommandTests` passes 4/4; evidence storage grants
and period-close acceptance remain open.
The new summary route is protected by `projects.project.view`, while mutations remain on the
high-risk manage permission.

WORK LOG (2026-09-14): Stage 26 shift-swap request and approval handlers now fail closed for
loaded shifts or swap requests outside the active company. `HrLifecycleTests` passes 13/13;
labour-cost integration and specialist review remain open.

WORK LOG (2026-09-14): Stage 26 roster publication now filters by active company before store
selection, canonical hashing and shift counting. `HrLifecycleTests` remains green at 13/13;
external distribution, labour-cost integration and specialist review remain open.

WORK LOG (2026-09-14): Stage 25 employee-document record, list and download authorization paths
now explicitly enforce the active company. `EmployeeDocumentTests` passes 8/8; storage adapter,
retention/deletion and full PostgreSQL acceptance remain open.

WORK LOG (2026-09-14): Broad verification after the Stage 25/26 hardening passes: the full unit
suite is 1,558/1,558 and architecture tests are 85/85. Existing warnings remain non-fatal; the
open-stage integration, specialist-review and workflow gates remain tracked per task.

WORK LOG (2026-09-14): Stage 18 inspection replay now compares inspection-plan identity in the
immutable payload, refusing changed-plan reuse of an operation id. Quality hold/inspection tests
pass 13/13; real held-stock evidence and specialist review remain open.

WORK LOG (2026-09-14): Stage 21 payment event replay now compares checkout, provider payment,
status and provider-reference fields as well as company and fingerprint. `EcommerceDomainTests`
passes 10/10; gateway orchestration and PostgreSQL webhook replay remain open.
