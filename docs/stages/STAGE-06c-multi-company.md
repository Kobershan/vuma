# STAGE 06c — Multi-company foundation: the registry, database-per-company, routing and migration fan-out

**Status:** NOT_STARTED · **Depends on:** 01 (persistence core), 04 (sync), 06, 07 · **Reference reading:** `docs/MULTI_COMPANY.md` §1, §2, §9, `docs/DECISIONS.md` ADR-099, ADR-116, ADR-117, ADR-118, ADR-120, `CLAUDE.md` §3 (R11), §7 rule 20

## Objective

Turn a one-database product into a many-database one **without changing what any existing module
believes about the database it is talking to**. Every handler still opens one `VumaRetailDbContext` and
still sees one company's data; what changes is who decides which database that context points at.

This stage builds the registry, the connection routing, the provisioning lifecycle and the migration
fan-out. It builds **no group features** — those are 06d. Splitting them is deliberate: the plumbing
here must be boring, complete and verified before anything interesting is built on top of it.

## What this stage does not own

- **The saga coordinator, dispatch, retry and compensation logic.** 06c builds the `registry.saga_intents`
  / `registry.saga_legs` tables and the registry outbox *as data* — nothing here writes an intent, reads
  one back, or drives a leg to acknowledgement. `ISagaCoordinator` and everything that calls it is 06d.
- **Credit groups, the barcode routing index, group read models.** 06d.
- **The Operator ID and `CompanyLink` — which companies may cooperate at all.** 06e.
- **Cross-company money, reservations, split invoicing, consolidated picking, field sales.** 07c, 08c,
  13b, 14b — all of which dispatch through 06d's coordinator, not through anything built here directly.
- **Where `Tenant` sits.** `Tenant` (and Stage 04b's licensing/lease rows) stay exactly where they are —
  replicated into and enforced from *each* company database, unchanged. The registry does not become a
  second source of licensing truth; a company database that cannot reach the registry must still be able
  to answer "am I read-only" from its own rows. `registry.companies` is a separate, registry-owned,
  cross-company-visible projection populated at provisioning — the group's address book, not the
  licensing root. See ADR-139.
- **A UI.** `/api/v1/companies` and `/api/v1/admin/migrations` are the whole of this stage's surface;
  Stage 08b/09's shell is what will eventually let an operator click through it.

## Deliverables

**The registry database**
- Its own `VumaRegistryDbContext`, its own migration chain, its own connection string, migrated first.
- `registry.companies` — code, legal name, trading name, registration number, tax number, base currency,
  locale, document prefix, connection details (encrypted at rest), `schema_version`, `lifecycle_state`,
  `is_active`.
- `registry.company_groups` / `_members` — the sets consolidation and group scope operate over.
- `registry.saga_intents` / `registry.saga_legs` / the registry outbox — the machinery 06d, 07c and 08c
  all dispatch through. Built here, used there.

**Routing**
- `ICompanyConnectionResolver` — company id → connection details, from the registry, cached with an
  invalidation path. **Never from a config file** (ADR-118).
- `ICompanyDbContextFactory` — opens a context against exactly one company.
- `ICompanyContext` — resolves the acting company from the session, terminal or device, the way
  `ITenantContext` resolves the tenant. Not a parameter a caller can forget.
- `ICompanyFanOut` — bounded-parallelism read across several companies, returning **per-company results
  including failures**. One company down returns four answers and one error, never a 500.
- **Architecture test: no command handler resolves two companies' contexts.** This is the enforcement
  behind "there is no cross-database transaction" (ADR-116) and it is the most important test in the
  stage.

**Provisioning** (ADR-118)
- `ProvisionCompanyCommand`: create database → apply migration chain → seed chart of accounts, document
  numbering, tax rules, permissions → register connection → activate. Lifecycle
  `Provisioning → Seeding → Registered → Active`, recorded, re-runnable from the failed step.
- Business operations see `Active` companies only. `DeactivateCompanyCommand` makes a company read-only;
  removal is separate, deliberate and export-first.

**Migration fan-out** (ADR-117)
- The runner iterates every company database, registry first, recording `schema_version` and
  `migration_state` per company, with bounded parallelism and progress output.
- **A company behind the running binary is not served** — its endpoints return an actionable failure
  naming the company and the pending migration.
- `Down` is exercised per company database in the test suite exactly as it is today.

**Backup / sync per database** (ADR-120)
- Snapshot schedules, retention and encryption per company database plus the registry; the snapshot
  ledger records the set. Restore of one company database is supported on its own.
- Sync cursors, outbox and inbox are per company database; the cloud mirrors the layout.

**Retrofit**
- `company_id` on every business row, backfilled. Redundant given the database boundary, and kept
  anyway: it makes a restored or exported database self-describing and it is the key the 06d
  projections use.
- The existing single-tenant installation migrates to **one company** and behaves exactly as before.

**API** — `/api/v1/companies` (list, provision, activate, deactivate), `X-Vuma-Company` header or
terminal binding selecting the acting company, and `/api/v1/admin/migrations` reporting per-company state.

**Permissions** — `company.view`, `company.provision`, `company.manage`, `platform.migrate`.

**Entitlement** — `MultiCompany` flag; not entitled → exactly one company and provisioning 404s. Metering:
active company count.

## Business rules

1. No command handler opens a transaction against more than one database. Enforced by test, not by care.
2. A company is visible to business operations only at `Active`.
3. Connection details live in the registry, encrypted, and never appear in a support export, a log or a
   telemetry payload (R10).
4. A company whose schema is behind the binary is refused with a named reason, never served.
5. Provisioning is re-runnable; there is no half-registered company.
6. A fan-out read degrades per company; the caller is told which company failed.
7. The registry is snapshotted more often than the companies it points at — losing it is
   disproportionate.

## Parts — the build list

- [ ] **A — The registry database.** `src/VumaRetail.Domain/Registry/` (`RegistryCompany`,
  `CompanyLifecycleState`, `CompanyGroup`, `CompanyGroupMember`, `SagaIntent`, `SagaLeg`,
  `RegistryOutboxMessage`), `Schemas.Registry`, `VumaRegistryDbContext` and its own migration chain and
  `IDesignTimeDbContextFactory`, `IConnectionSecretProtector` (AES-256-GCM) for the encrypted connection
  column. No behaviour beyond what EF needs to persist the rows — the saga machinery is 06d's.
- [ ] **B — `CompanyId` retrofit.** Add `CompanyId` to `Entity` alongside `TenantId`, stamped by the
  persistence layer from the ambient `ICompanyContext` (the same mechanism that stamps `RowVersion` —
  see ADR-140 for why this is not a constructor parameter threaded through ~90 call sites). One migration
  adds the column to every existing table; existing tests stay green because every row in today's single
  database resolves to the one company `ICompanyContext` reports.
- [ ] **C — Routing seams.** `ICompanyConnectionResolver`, `ICompanyDbContextFactory`, `ICompanyContext`,
  `ICompanyFanOut` in `Application.Abstractions`, implementations in `Infrastructure`, DI registration.
  The architecture test: no command handler resolves two companies' `DbContext`s.
- [ ] **D — Provisioning.** `ProvisionCompanyCommand`/handler driving
  `Provisioning → Seeding → Registered → Active`, re-runnable from the failed step;
  `DeactivateCompanyCommand`.
- [ ] **E — Migration fan-out.** `ICompanyMigrationRunner`, per-company `schema_version`/
  `migration_state` in the registry, the "company behind the binary is not served" refusal.
- [ ] **F — Backup/sync per database.** Extend Stage 04's snapshot and cursor machinery to iterate
  companies; the registry snapshotted on its own, more often.
- [ ] **G — API, permissions, entitlement.** `/api/v1/companies`, `X-Vuma-Company` header resolution,
  `/api/v1/admin/migrations`; `company.view`/`company.provision`/`company.manage`/`platform.migrate`;
  the `MultiCompany` entitlement flag and its metering counter.
- [ ] **H — The existing installation.** A registry seed that migrates today's single-tenant install to
  exactly one `Active` company, so nothing existing changes behaviour.

Parts are ordered by dependency (C needs A; D needs A, B and C; E needs A and D; H needs everything before
it). Each is its own green checkpoint and its own commit.

## Tests / acceptance

- Provision three companies (hardware, DC, groceries) from empty; each gets its own database, its own
  numbering starting at 1, and its own seeded chart of accounts.
- The existing seeded single-company database migrates to one company and **every existing test stays
  green**. This is the stage's real bar.
- Kill the provisioning step after "database created": re-running completes it, and the company was
  never `Active` in between.
- Migrate a tenant where one company database is unreachable: the others migrate, that one is recorded
  as pending, and its endpoints refuse with the named migration.
- A fan-out over three companies with one database stopped returns two results and one error.
- The architecture test fails a deliberately introduced two-context handler.
- Restore one company database from a snapshot while the other two keep trading.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] Every existing stage's tests green after the retrofit
- [ ] Migration reversible per company database, `Down` executed
- [ ] `docs/DATA_MODEL.md` §2b and §3 reflect the per-database layout
- [ ] `docs/SYNC_AND_BACKUP.md` updated for per-database cursors, snapshots and restore
- [ ] `multi-company-guard` and `architecture-guard` run, findings closed
- [ ] Seed: three provisioned companies, demonstrable with `scripts/seed.ps1`
