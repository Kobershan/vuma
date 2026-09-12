# STAGE 28 — Projects, Contracts and Job Costing

**Status:** NOT_STARTED — specification created 2026-09-12, implementation not certified · **Depends on:** 07, 26; integration with 05, 10c, 12 · **Reference reading:** [finance stage](STAGE-07-finance.md), [workforce stage](STAGE-26-workforce-management.md), [invoice stage](STAGE-10c-quotes-invoices-analytics.md); [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

Track project budgets, authorized costs, WIP, contract variations, milestone billing and rebates with reproducible company/currency reporting.

## What this stage does not own

Workforce owns attendance/time capture; Finance owns recognition and GL; Sales owns invoice issuance; Procurement owns purchase commitments. Projects links those records and applies approved project policies.

## Deliverables

### Domain and client model

`Project`, `ProjectBudget`, `ProjectCostEntry`, `ProjectContract`, `ContractVariation`, `BillingMilestone`, `RebateAgreement`. Start in `src/VumaRetail.Domain/Projects/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

`ApproveProjectBudgetCommand`, `AllocateProjectCostCommand`, `ApproveContractVariationCommand`, `BillMilestoneCommand`, `CloseProjectCommand`. Ports: `IProjectRepository`, `IJobCostReadModel`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

`/api/v1/projects`, `/{id}/budgets`, `/{id}/costs`, `/{id}/milestones`, `/api/v1/project-contracts`, `/api/v1/rebate-agreements`. These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `projects.view`, `projects.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Budget, committed cost, actual cost and invoiced revenue are distinct measures with explicit currency and AsAt.
2. Approved timesheet/receipt references are unique per allocation; reversal appends a negative cost entry referencing its source.
3. An unapproved variation changes neither committed budget nor invoice amount.
4. One milestone produces one fiscal invoice per supplying company through Stage 10c; retry returns the same result.
5. WIP/recognition methods are finance-approved policy inputs; closing requires reconciliation and handling of open commitments.
6. Cross-company work uses separately authorized allocations and sagas, not joins or transactions across books.
7. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [ ] 28-P01: Implement projects/contracts, budget versions and approval transitions.
- [ ] 28-P02: Integrate labour/procurement cost allocation, WIP and milestone billing.
- [ ] 28-P03: Add rebate calculation/reconciliation, APIs and scoped job-cost reports.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Job_cost_preserves_measures`: budget ZAR 10,000, actual ZAR 2,000 and separate open commitment ZAR 3,000; available budget is ZAR 5,000.
- `Milestone_bills_once`: approved ZAR 4,000 milestone retried three times produces one invoice.
- `Unapproved_variation_cannot_bill`: proposed ZAR 1,000 variation leaves billable contract value unchanged.
- `Cost_reversal_is_a_new_entry`: reverse a ZAR 200 labour allocation; original remains, offset is -200 and net is zero.
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

