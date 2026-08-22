# STAGE 06c — Multi-company group core

**Status:** NOT_STARTED · **Depends on:** 06, 07 · **Reference reading:** `docs/MULTI_COMPANY.md` §1–§3, §6, `CLAUDE.md` §3 (R11, R12), §7 rules 3, 12, 18, `docs/DATA_MODEL.md` §3, §4d, `docs/DECISIONS.md` ADR-099, ADR-100, ADR-101

## Objective

Introduce the **company** as a first-class level between tenant and store, retrofit company scoping
across every existing module, and build the two group-level services that later stages consume: the
group barcode/catalogue resolver and the group credit limit.

This stage writes little business logic and touches almost every file. That is the point: doing it now
costs one stage, and doing it after 14b, 21 and 29 costs a rewrite.

## Deliverables

**Domain**
- `Company` — legal name, trading name, company registration number, VAT number, base currency, locale,
  logo, letterhead, document-number prefix, `IsActive`. Belongs to a tenant.
- `CompanyGroup` — an ordered set of companies for consolidation and group scope. A tenant may have
  more than one group; a company may be in one group.
- `CreditGroup` / `CreditGroupMember` — the shared limit and its per-company members, for **both**
  customers and suppliers (`Direction: Receivable | Payable`). Members hold optional sub-limits.
- `CompanyScope` value object — `Company(id)` or `Group(ids)`. There is no "all data" scope.

**Application**
- `ICompanyContext` — resolves the acting company from the session/terminal/device, the way
  `ITenantContext` resolves the tenant. Never a parameter a caller may forget.
- `ICompanyDataSource` — the only way to read another company's data. Group-scoped, permission-gated,
  audited. An architecture test fails the build on any repository query that filters `company_id`
  by hand outside this service.
- `IGroupCreditService` — `GetPosition(creditGroupId)`, `TryConsume(...)`, `Release(...)`. Consumption
  is a serialisable transaction on the credit-group row (ADR-101).
- `IBarcodeResolver` — `Resolve(barcode, scanningContext)` → one match, or `MultipleCompanyMatches`
  with every candidate. Never guesses (ADR-100).
- Commands: create/update company, activate/deactivate, create credit group, add/remove member, set
  group limit, set member sub-limit. All through command handlers, all permission-gated.

**Infrastructure**
- `company_id uuid` added to every business table, with a global query filter alongside the tenant
  filter. Nullable only on platform/identity/licensing/sync tables that are genuinely tenant-wide;
  an architecture test enumerates the exemptions so a new one cannot be added silently.
- `group.barcode_index` — projection over `catalog.barcodes` maintained by the outbox, unique index on
  `(tenant_id, barcode, company_id)`, lookup index on `(tenant_id, barcode)`.
- Document number counters keyed by `(company_id, document_type)` — `finance.document_number_counters`
  gains `company_id` and the existing per-tenant rows migrate to the tenant's default company.
- Migration: additive, reversible, and it **backfills** every existing row to a default company created
  per tenant from the existing tenant record. An existing single-company installation must come out of
  this migration behaving exactly as it did going in.

**API** — `/api/v1/companies`, `/api/v1/credit-groups`, `/api/v1/scan/{barcode}`; `X-Vuma-Company`
header (or terminal binding) selects the acting company, and group-scoped endpoints declare it in
OpenAPI.

**Permissions** — `company.view/manage`, `group.view`, `group.credit.view/manage`, `group.report`.

**Entitlement** — `MultiCompany` module flag. Not entitled → exactly one company, every group endpoint
404s, and nothing else changes. Metering counter: companies active, group-scoped queries per day.

## Business rules

1. A store belongs to exactly one company; a company has zero or more stores.
2. Every business row has a company. There is no "shared" business data.
3. A document number sequence is per company. Two companies may legitimately both have `INV-000001`.
4. Group scope requires a permission and is written to the audit trail with the scope used.
5. Group credit available = group limit − Σ member exposure; a member sub-limit narrows, never widens.
6. Concurrent consumption of the same credit group is serialised. Two sales cannot both take the last
   of a limit.
7. Barcode collisions across companies resolve by session company → availability at the scanning
   location → operator choice. Never silently.
8. Deactivating a company blocks new documents and leaves existing ones readable and reportable.

## Tests / acceptance

- A tenant with three companies: a document raised in each gets three independent number sequences.
- The migration applied to a seeded single-company database leaves every existing test green.
- `Down` runs and the database returns to the pre-company shape.
- Two parallel `TryConsume` calls against a group with R5 000 available and two R4 000 demands: exactly
  one succeeds. Real database, real transactions, not a mock.
- A barcode present in two companies returns both candidates when the scanning context cannot decide.
- A barcode present in one company resolves in a single index probe — asserted with a query counter.
- An architecture test proves no repository outside `ICompanyDataSource` filters `company_id` manually.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] Every existing stage's tests still green after the retrofit — this is the stage's real bar
- [ ] Migration reversible, backfill verified against a seeded database
- [ ] `docs/DATA_MODEL.md` §3 and §4 updated with `group` schema and the `company_id` column rule
- [ ] `docs/SYNC_AND_BACKUP.md` registry updated for `Company`, `CompanyGroup`, `CreditGroup`,
      `CreditGroupMember`, `BarcodeIndexEntry`
- [ ] `multi-company-guard` and `architecture-guard` run and their findings closed
- [ ] Seed data: three companies (hardware, DC, groceries), one shared customer with a group limit
