# STAGE 23 — Service Management

**Status:** IN_PROGRESS — ticket/SLA slices, bounded breach worker, durable breach audits and company-scoped breach reads/API are implemented; full API/isolation acceptance and closure review remain · **Depends on:** 14; integration with 05, 07, 08, 24 · **Reference reading:** [order stage](STAGE-14-order-management.md), [workflow stage](STAGE-05-workflow.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8.

2026-09-13: Added company-scoped SLA deadline query/API support. Response and resolution deadlines
use the configured weekday business-hours clock, and breach flags are evaluated against an explicit
as-of timestamp. `ServiceSlaClockTests` passes 7/7; the bounded breach worker is registered and
durable breach/audit persistence and full acceptance remain.

## Objective

Handle customer tickets, warranty checks, repair jobs, RMA and service parts with traceable custody, SLA tracking and correct financial treatment.

## What this stage does not own

Orders owns returns, Warehouse owns parts movements, Finance owns postings, Workflow owns approvals and Logistics owns shipment/POD. Service coordinates those capabilities.

## Deliverables

### Domain and client model

`ServiceTicket`, `WarrantyClaim`, `RepairJob`, `ServicePartUsage`, `ServiceSla`, `ServiceCustodyEvent`. Start in `src/VumaRetail.Domain/Service/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model. Waiting-for-customer periods are now accumulated in working hours and applied to SLA deadlines.

### Application

`OpenServiceTicketCommand`, `ApproveWarrantyClaimCommand`, `IssueServicePartCommand`, `CompleteRepairCommand`, `CloseServiceTicketCommand`. Ports: `IServiceRepository`, `IServiceSlaClock`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

`/api/v1/service/tickets`, `/warranties`, `/repairs`, `/parts`, `/slas`, `/custody`; own-ticket public DTOs only via the public host. These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `service.ticket.view`, `service.ticket.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Warranty uses the original sale/date/serial snapshot and policy; returned or mismatched serials do not receive a second entitlement.
2. Customer-owned goods in repair are custody records, not company-owned saleable stock.
3. Parts issue requires availability and posts cost to the owning company; a retry does not issue twice.
4. SLA uses the configured business calendar; waiting-for-customer pauses only under an explicit policy with an audit event.
5. A paid repair quote requires customer acceptance before work/capture; refunds and replacements reuse existing commands.
6. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [~] 23-P01: Implement tickets, warranty snapshots and custody lifecycle — [TASK-23-001](../tasks/TASK-23-001-service-ticket-warranty-custody.md) and [TASK-23-005](../tasks/TASK-23-005-service-read-api.md) are implemented; domain, persistence, commands, scoped reads and initial API routes are present, while full isolation acceptance remains.
- [~] 23-P02: Integrate repair approvals, reserved/consumed parts, invoicing and RMA. Reservation-backed
  service-part issue, dedicated stock movement/reference, API route, and replay test are implemented;
  invoicing/RMA and full PostgreSQL availability acceptance remain.
- [~] 23-P03: Add SLA worker, service APIs and customer/company isolation acceptance. Deterministic
  weekday business-hours calculation, customer-wait pause accounting, and a bounded tenant/company-
  scoped breach worker and append-only, unique breach-audit persistence are implemented; API and
  full isolation acceptance remain.

## Progress evidence

- 2026-09-13: Added `ServiceTicket`, `WarrantyClaim`, and append-only `ServiceCustodyEvent`
  domain records under `src/VumaRetail.Domain/Service/`. The warranty serial is a frozen sale
  snapshot; customer custody is not a stock ledger entry; ticket closure is lifecycle-gated.
- 2026-09-13: Added persisted SLA policy creation with duplicate-name protection and the permissioned
  `POST /api/v1/service/slas` route. Service-focused unit tests: **28/28 passed**.
- Focused domain tests: **3/3 passed** in `ServiceDomainTests`.
- Remaining: EF mappings/migration, repositories, commands, permissions/entitlements, API routes,
  parts and financial integration, SLA clock, PostgreSQL acceptance, and specialist review.
- 2026-09-13: Service-part issue now reserves availability, posts `ServicePartIssue`/`ServicePart`
  inventory facts, consumes the hold, and rejects changed-content operation replays; focused command
  tests pass **3/3**.
- 2026-09-13: Added `IServiceSlaClock` and `BusinessHoursServiceSlaClock`; weekday 09:00–17:00 UTC
  elapsed-time boundaries pass **2/2**. Waiting-for-customer pause accounting is persisted.
- 2026-09-14: Service migration Up/Down acceptance passes **1/1** against PostgreSQL. Worker,
  financial integration, and specialist closure evidence remain open.
- 2026-09-14: Warranty claim approval now validates the loaded claim tenant as well as the active
  company before changing state. `ServiceCommandTests` passes **4/4**.
- 2026-09-14: Repair completion and ticket resume/close now validate the loaded record tenant as
  well as company scope before changing state. StoreServer build passes with **0 errors**.
- 2026-09-14: SLA deadline reads now reject tickets outside the active tenant before calculating
  deadlines. Service unit tests pass **33/33**.
- 2026-09-15: Registered `IServiceSlaWorker` evaluates open-ticket breaches with persisted pause
  accounting and tenant/company guards; `ServiceSlaClockTests` passes **7/7**.
- 2026-09-15: SLA breach observations are now append-only and durable in
  `service.service_sla_breach_events`, uniquely keyed per tenant/ticket/SLA/type; migration
  Up/Down acceptance passes **1/1**.
- 2026-09-17: `ServiceCustodyIsolationTests` passes **1/1** on PostgreSQL, proving the custody
  export/query boundary excludes another company and another tenant while returning the active
  tenant/company row.
- 2026-09-17: Service SLA-deadline API now binds the requested company before dispatching the
  company-guarded query. StoreServer Release build passes with **0 errors** and the focused
  service/HR/assets unit slice passes **162/162**.
- 2026-09-18: Added company/tenant-scoped warranty, repair and service-part usage reads at
  `GET /api/v1/service/warranties`, `/repairs` and `/parts`. Unit coverage includes tenant
  isolation, active-company refusal and cost/currency preservation.
- 2026-09-18: TASK-23-005 verification is complete. Service unit tests pass **43/43**; the
  StoreServer Release build passes with **0 warnings, 0 errors**; PostgreSQL-backed service
  migration, API contract, and custody-isolation tests pass **3/3**. Remaining Stage 23 work is
  invoicing/RMA integration and final closure acceptance.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Customer_property_does_not_inflate_stock`: receive one customer laptop for repair; company stock valuation is unchanged.
- `Part_retry_does_not_double_issue`: issue two parts at ZAR 30 each, retry; usage is 2 and cost is ZAR 60.
- `Warranty_serial_must_match`: serial B cannot claim against serial A's sale.
- `Paused_sla_excludes_wait`: 8-hour SLA, 2 working hours elapsed plus 24 hours waiting-for-customer; 6 working hours remain.

- 2026-09-13: `BusinessHoursServiceSlaClock` now also computes deterministic SLA deadlines across
  closing times and weekends; focused service tests cover forward deadline calculation and negative
  duration refusal.
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
