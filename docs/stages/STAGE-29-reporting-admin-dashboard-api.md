# STAGE 29 — Reporting and Admin Dashboard API

**Status:** NOT_STARTED — specification created 2026-09-12, implementation not certified · **Depends on:** each contributing module's verified API/event contract; 03, 04, 06c, 07 · **Reference reading:** [API standards](../API_STANDARDS.md) §§1–10, [sync contract](../SYNC_AND_BACKUP.md) §§3–7, [cloud/offline recommendations](../OFFLINE-CLOUD-API-AND-PROTECTION.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

Provide authorized local and cloud reports, scheduled exports and the dashboard API consumed by mobile clients. Publish each contributing module incrementally; report unavailable contributors explicitly.

## What this stage does not own

Modules own transactions; reports never recompute historical pricing/tax from current catalogues. Vendor monitoring belongs to isolated Stage 30b, not tenant dashboards.

## Deliverables

### Domain and client model

`ReportDefinition`, `ReportRun`, `ProjectionCheckpoint`, `DashboardSnapshot`, `ScheduledReport`. Start in `src/VumaRetail.Domain/Reporting/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

`ScheduleReportCommand`, `RequestReportExportCommand`, `RebuildReportProjectionCommand`. Ports: `IReportingProjection`, `IReportExporter`, `IDashboardReader`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

`GET /api/v1/dashboard/overview`, `GET /api/v1/reports/{code}`, `POST /api/v1/report-exports`, `GET /api/v1/report-exports/{id}`, `/api/v1/report-schedules`. These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `reporting.view`, `reporting.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Every result exposes tenant/company/store scope, currency, business date/timezone, AsAt and missing/stale contributors.
2. Sales metrics distinguish orders, completed POS sales and issued invoices; a draft order is not realized revenue.
3. Never sum different currencies without an explicit reporting currency and snapshotted FX policy.
4. Projection checkpoints and event IDs deduplicate replay; rebuilds use a new generation then atomically switch readers.
5. Authorization restricts allowed companies before query/aggregation; export downloads require an authorized recipient and expiring access.
6. Default page size 50, maximum 200; export requests return 202 and an operation ID; heavy reports execute outside trading request transactions.
7. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [ ] 29-P01: Specify financial/business-date definitions and per-module projection contracts.
- [ ] 29-P02: Build local/cloud projections, checkpoints and scope-aware dashboard/report APIs.
- [ ] 29-P03: Add export scheduling, mobile contract tests, rebuild and stale-data acceptance.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Dashboard_never_adds_rand_to_dollars`: ZAR 100 and USD 10 appear separately; without FX policy the system never reports ZAR 110.
- `Business_day_uses_store_timezone`: Johannesburg transaction at 00:30 is included in its local day although UTC is the previous date.
- `Cloud_reports_staleness`: one store last synced 24 hours ago; aggregate identifies that contributor and cannot label itself live.
- `Projection_replay_is_stable`: replay 1,000 source events twice; totals match one delivery; rebuild yields the same totals.
- `Other_tenant_and_unauthorized_company_are_denied`: authenticated tenant A/company A cannot read, mutate, export or enqueue for tenant B/company B by changing an ID.
- `Replay_with_different_content_is_rejected`: reuse a completed operation ID with changed input; return a stable conflict and preserve the original result.
- Execute migration Up/Down on a disposable database, permission-denial tests on every high-risk route and module read-only behavior. Client-only changes mark database checks not applicable with a reason.

## Exit checklist

- [ ] Every listed rule and scenario has executed evidence, including outage/replay and authorization.
- [ ] Planned API routes are verified against the actual host's OpenAPI and real client contracts.
- [ ] Per-company accounting/stock, retention and audit requirements are satisfied where applicable.
- [ ] Seed/demo, migration reversibility, backup implications and module replication registration are evidenced.
- [ ] Relevant specialist reviews from [AGENTS](../AGENTS.md) are recorded; missing tooling is UNVERIFIED, not an invented review.
- [ ] `CLAUDE.md` §8 is met, measured results are recorded and unresolved release blockers remain open.

**Verification boundary:** this document was reviewed for scope and links only. No stage implementation, live API, UI, migration or production vendor integration was certified in the 2026-09-12 audit.
