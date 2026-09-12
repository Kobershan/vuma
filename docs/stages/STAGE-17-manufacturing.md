# STAGE 17 — Manufacturing Execution

**Status:** IN_PROGRESS — canonical task queue created 2026-09-12; implementation not certified · **Depends on:** 16, 13; integration with 07, 08c, 05 · **Reference reading:** [BOM stage](STAGE-16-bom-setup.md), [warehouse stage](STAGE-13-warehouse-management.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

Turn approved BOM versions into traceable work orders, material consumption, finished stock and reconciled WIP. Production continues against the local store server while the internet is unavailable; cloud reporting receives committed events.

## What this stage does not own

Stage 16 owns BOM definitions and cost inputs; Stage 08 owns stock movements and reservations; Stage 07 owns accounts and journal posting; Stage 18 owns quality disposition.

## Deliverables

### Domain and client model

`ProductionOrder`, `ProductionMaterialIssue`, `ProductionReceipt`, `ProductionScrap`, `ProductionGenealogy`, `CapacitySlot`. Start in `src/VumaRetail.Domain/Manufacturing/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

`ReleaseProductionOrderCommand`, `IssueProductionMaterialCommand`, `ReceiveProductionOutputCommand`, `RecordProductionScrapCommand`, `CloseProductionOrderCommand`. Ports: `IProductionRepository`, `IProductionCostService`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

`POST /api/v1/manufacturing/production-orders`, `POST /{id}/release`, `POST /{id}/issues`, `POST /{id}/receipts`, `POST /{id}/scrap`, `POST /{id}/close`, `GET /{id}/genealogy` (suffixes under production-orders). These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `manufacturing.view`, `manufacturing.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Draft → Released → InProgress → Completed → Closed; cancel only before irreversible postings, otherwise append reversal/compensation records.
2. Release snapshots the published BOM version, routing, quantities and cost basis; editing a later BOM version cannot alter an existing order.
3. Reserve using Stage 08c, consume through the owning company's stock ledger; a shortage refuses the issue atomically.
4. Every receipt and scrap event has an operation ID. Duplicate submissions create no extra stock or WIP postings.
5. Close only when consumed material, finished output, scrap and explicit variance reconcile; capacity overrides require a separate permission and reason.
6. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [ ] 17-P01: Snapshot BOM and define production lifecycle and repositories.
- [ ] 17-P02: Implement reservation, issue, receipt, scrap and per-company financial event integration.
- [ ] 17-P03: Add capacity/genealogy queries, API routes, offline replay and closure evidence.

Execute parts in this order. The canonical queue is [STAGE-17-INDEX](../tasks/STAGE-17-INDEX.md), with
focused tasks [TASK-17-001](../tasks/TASK-17-001-production-order-lifecycle.md),
[TASK-17-002](../tasks/TASK-17-002-production-material-and-output.md), and
[TASK-17-003](../tasks/TASK-17-003-production-api-and-closure.md). Record any durable change to
existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Ten_units_consume_twenty_components_once`: BOM requires 2 components/unit; reserve 20 for 10 finished units; duplicate issue/receipt requests leave consumption 20 and output 10.
- `Scrap_reconciles_wip`: issue cost ZAR 200; receipt value ZAR 180 and scrap ZAR 20; closing WIP is zero and every journal balances.
- `Bom_changes_do_not_reprice_released_order`: version 2 changes requirement to 3; an order released against version 1 still requires 2.
- `Offline_production_replays_once`: capture two local issues during a 24-hour cloud outage; reconnect twice; cloud sees two issues, not four.
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
