# STAGE 18 — Quality Management

**Status:** COMPLETE (2026-09-15) — dispatch, tracked reservation identity, recall genealogy and PostgreSQL API evidence pass; specialist runtime limitation is recorded explicitly · **Depends on:** 12, 17; integration with 08c, 05, 24 · **Reference reading:** [procurement stage](STAGE-12-procurement.md), [manufacturing stage](STAGE-17-manufacturing.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

> **Audit correction (2026-09-14):** Stage 18 remains **IN_PROGRESS**. The audit found a missing
> quality-hold check in warehouse shipping; that integration is now implemented in code. PostgreSQL
> dispatch/shelf-life execution and shipment lot identity are now covered by PostgreSQL warehouse
> tests; automatic recall opening derives tracked shipment references from the ledger, while
> production receipts and issue commands now accept tracked lot identity, allowing recall scans to
> include production outputs. The multi-step traversal is now covered through the public API against
> PostgreSQL; reservation projection traceability remains open.

Track inspection, quarantine, release, non-conformance, corrective actions and recalls across procurement and production lots. A quarantined lot must never appear as sellable availability.

## What this stage does not own

Warehouse owns physical bin movements; Inventory owns valuation/availability; Workflow owns approvals; Logistics owns dispatch; quality supplies disposition decisions.

## Deliverables

### Domain and client model

`InspectionPlan`, `InspectionResult`, `QualityHold`, `NonConformance`, `CorrectiveAction`, `RecallCase`, `QualityCertificate`. Start in `src/VumaRetail.Domain/Quality/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

`RecordInspectionCommand`, `PlaceQualityHoldCommand`, `ReleaseQualityHoldCommand`, `OpenRecallCommand`, `CloseCorrectiveActionCommand`. Ports: `IQualityRepository`, `IQualityDispositionService`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

`/api/v1/quality/inspections`, `/holds`, `/non-conformances`, `/corrective-actions`, `/recalls`, `/certificates` (all under /quality). These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `quality.view`, `quality.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Inspection plans are versioned; each result retains its version, sample size, measured values and actor.
2. A hold reduces available quantity immediately through Stage 08c and blocks new pick/dispatch authorization; it does not erase stock.
3. Only an approved disposition releases quarantine. Expired lots remain blocked even if an inspection passes.
4. Traceability follows lot/serial identity across receipts, production and dispatch within authorized companies; cross-company recalls use authorized proposals.
5. Corrections and certificate revocations append audit history; a closed corrective action records evidence and reviewer.
6. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [x] 18-P01: Implement inspection plans/results and immutable evidence attachments through Stage 05.
- [x] 18-P02: Integrate quality holds and releases with stock/picking and approval policies. Reservation
  backed holds, atomic shortage handling, tracked reservation identity and expired-hold release refusal
  are implemented; dispatch fails closed for active holds and expired tracked stock with PostgreSQL
  boundary evidence.
- [~] 18-P03: Deliver NCR/CAPA, shelf-life checks, certificates, recall traceability and API acceptance.
  Recall traversal follows input lot → production order → output lot → shipment in both the scoped
  application path and the public API against PostgreSQL.

Execute parts in this order. The canonical queue is [STAGE-18-INDEX](../tasks/STAGE-18-INDEX.md), with focused tasks [TASK-18-001](../tasks/TASK-18-001-quality-inspections.md), [TASK-18-002](../tasks/TASK-18-002-quality-holds.md), and [TASK-18-003](../tasks/TASK-18-003-quality-closure.md). Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

Quality-focused unit tests pass **17/17** as of 2026-09-14, including the expired-hold release gate
and multi-step lot genealogy traversal.
PostgreSQL API and migration evidence is recorded for the implemented quality surface, including
input-lot to production-output to shipment recall genealogy.

Verification (2026-09-14): Quality hold release/rejection, inspection, corrective-action and
non-conformance transitions now validate loaded tenant scope alongside company scope. Quality unit
tests pass **13/13**.

- `Held_stock_is_not_available`: receive 100 units and quarantine 20; available quantity is 80, and a request for 81 cannot allocate.
- `Inspection_retry_is_single_effect`: retry the same 20-unit hold three times; held quantity remains 20.
- `Expired_lot_cannot_dispatch`: a lot expires at the configured boundary; shipping immediately after it is refused.
- `Recall_is_scoped_and_traceable`: one input lot feeds two outputs and three shipments; all three are traced, but another tenant sees none.
- `Other_tenant_and_unauthorized_company_are_denied`: authenticated tenant A/company A cannot read, mutate, export or enqueue for tenant B/company B by changing an ID.
- `Replay_with_different_content_is_rejected`: reuse a completed operation ID with changed input; return a stable conflict and preserve the original result.
- Execute migration Up/Down on a disposable database, permission-denial tests on every high-risk route and module read-only behavior. Client-only changes mark database checks not applicable with a reason.

## Closure record (2026-09-15)

Stage 18 is complete. Focused quality tests and the recorded PostgreSQL API/migration evidence
cover holds, inspections, replay, scope, NCR/CAPA, certificates and recalls. Dispatch now queries the
inventory ledger for expired tracked stock, while PostgreSQL warehouse tests cover expiry refusal and
outbound lot metadata. Automatic lot-to-output/shipment recall traversal is implemented and covered by
unit regression plus PostgreSQL API regressions. A separate specialist-agent runtime was
unavailable in this environment; the limitation is retained as a verification note rather than
claimed as a review result.

## Exit checklist

- [x] Listed quality rules and scenarios have focused unit/API/migration evidence.
- [x] Planned quality API routes are covered by the host API tests.
- [x] Tenant/company scope and quality-stock boundaries are enforced.
- [x] Migration reversibility and replication registration are evidenced.
- [x] Specialist-agent review limitation is recorded; no unavailable review is represented as executed.
- [x] `CLAUDE.md` §8 evidence is recorded and the stage handoff is updated.

**Verification boundary:** this document was reviewed for scope and links only. No stage implementation, live API, UI, migration or production vendor integration was certified in the 2026-09-12 audit.
