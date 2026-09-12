# STAGE 27 — Assets, Maintenance and Store Operations

**Status:** NOT_STARTED — specification created 2026-09-12, implementation not certified · **Depends on:** 07, 25; integration with 05, 12 · **Reference reading:** [finance stage](STAGE-07-finance.md), [HR stage](STAGE-25-hr-management.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

Maintain asset custody and depreciation, maintenance work orders, leases and store operating checklists with audited evidence and company-specific books.

## What this stage does not own

Finance owns ledger policies and period close; HR owns people; Procurement owns purchases. Asset policy inputs require finance approval, not hard-coded tax assumptions.

## Deliverables

### Domain and client model

`FixedAsset`, `AssetBook`, `DepreciationRun`, `MaintenanceOrder`, `LeaseSchedule`, `StoreChecklist`, `ChecklistExecution`. Start in `src/VumaRetail.Domain/Assets/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

`CapitalizeAssetCommand`, `RunDepreciationCommand`, `DisposeAssetCommand`, `CompleteMaintenanceCommand`, `SubmitChecklistCommand`. Ports: `IAssetRepository`, `IDepreciationCalculator`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

`/api/v1/assets`, `/api/v1/maintenance/orders`, `/api/v1/store-operations/checklists`, `/api/v1/assets/depreciation-runs`. These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `assets.view`, `assets.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Each asset belongs to one legal company; physical relocation does not silently change legal ownership.
2. Depreciation is once per asset-book-period, currency explicit, with final-period rounding and residual floor.
3. Closed financial periods reject new postings; corrections use approved reversals in an open period.
4. Maintenance records labour/parts and approved capitalization versus expense, through Finance posting events.
5. Lease schedules store approved policy inputs; no statutory treatment is implied by a default calculation.
6. Offline checklist submissions preserve capture time, submit time and device identity and can be retried safely.
7. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [ ] 27-P01: Implement asset books, capitalization, custody and depreciation schedules.
- [ ] 27-P02: Integrate maintenance, leases, procurement and Finance posting events.
- [ ] 27-P03: Implement local checklist queue APIs, evidence attachments and period-close acceptance.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Depreciation_stops_at_residual`: cost ZAR 12,000, residual zero, useful life 12 months, straight-line monthly policy; 12 charges of ZAR 1,000, no thirteenth charge.
- `Depreciation_period_retry_is_noop`: run the same asset-book-period twice; one journal only.
- `Company_relocation_needs_transfer`: moving an asset between companies requires approved financial transfer; changing location alone cannot reassign books.
- `Offline_checklist_preserves_evidence`: 20 queued checklist results replay twice; exactly 20 records retain original capture timestamps.
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

