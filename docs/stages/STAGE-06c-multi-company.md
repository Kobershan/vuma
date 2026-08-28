# STAGE 06c — Multi-company foundation: the registry, database-per-company, routing and migration fan-out

**Status:** NOT_STARTED · **Depends on:** 01 (persistence core), 04 (sync), 06, 07 · **Reference reading:** `docs/MULTI_COMPANY.md` §1, §2, §9, `docs/DECISIONS.md` ADR-099, ADR-116, ADR-117, ADR-118, ADR-120, `CLAUDE.md` §3 (R11), §7 rule 20

## Stage architecture

**WHAT:** Registry, database-per-company routing/context, resumable provisioning and migration fan-out,
company identity retrofit, isolated backup/sync, and lifecycle APIs. **WHY:** Preserve existing module
semantics while making the database wall explicit; group business features remain Stage 06d.

**EXTENDS:** Stage 01 persistence, Stage 04 sync/backup, Stage 06 master data, Stage 07 finance,
`ITenantContext`, `VumaRetailDbContext`, and the locked topology in `CLAUDE.md`. **AFFECTED:** Domain
registry models; Application ports/commands/queries; Infrastructure registry/company contexts,
migrations, routing, backup and sync; Contracts/Web/StoreServer API; unit, architecture, integration,
API, sync and specialist tests.

**DATA:** One registry database plus one database per company. Registry owns identity, encrypted
connection metadata, lifecycle, groups, saga records and migration state. Company databases own books,
stock, numbering, audit, outbox/inbox and restore units. **API:** versioned company lifecycle and
migration status endpoints with company selection. **SECURITY:** tenant/permission checks, encrypted
secrets, no secret telemetry, lifecycle/schema serving guards. **MULTI-COMPANY:** no cross-database
transaction, 2PC, FDW, or cross-company foreign key; reads fan out with per-company failures and
writes use saga legs. **SYNC/OFFLINE:** cursors, queues and snapshots are database-scoped; terminal
queues retain company identity. **TESTING:** Domain/Application coverage, architecture, PostgreSQL,
API, fan-out, restore/redrive and specialist review. **ADRs:** 099, 116–120. New ADR only if a locked
boundary must change. **MUST NOT CHANGE:** existing module behavior, store authority, API-first rule,
UUID v7, immutable documents, or Stage 06d scope.

Insufficient requirements must be marked `NEEDS ARCHITECTURAL CLARIFICATION`; do not invent behavior.

## Planning notes and status conflicts

This planning pass found conflicting status records and does not silently resolve them: `CURRENT.md`
and the previous TASK-005 handoff said `IN_PROGRESS`, while this document and `PROGRESS.md` said
`NOT_STARTED`; `ROADMAP.md` states that `PROGRESS.md` is authoritative for live stage status. The
previous TASK-005 work log also described source implementation already performed, which is outside
this planning-only request and is not treated as a verified Stage 06c completion. The task index records
the intended execution order; implementation sessions must revalidate the working tree before coding.

## Objective

Turn a one-database product into a many-database one **without changing what any existing module
believes about the database it is talking to**. Every handler still opens one `VumaRetailDbContext` and
still sees one company's data; what changes is who decides which database that context points at.

This stage builds the registry, the connection routing, the provisioning lifecycle and the migration
fan-out. It builds **no group features** — those are 06d. Splitting them is deliberate: the plumbing
here must be boring, complete and verified before anything interesting is built on top of it.

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

## Task dependency graph

- [ ] **06c.1 Registry foundation** — canonical [TASK-06C-01](../tasks/TASK-06C-01-registry-foundation.md).
- [ ] **06c.2 Registry groups and saga records** — canonical [TASK-06C-02](../tasks/TASK-06C-02-registry-saga-records.md).
- [ ] **06c.3 Company connection routing** — canonical [TASK-06C-03](../tasks/TASK-06C-03-company-routing.md).
- [ ] **06c.4 Company context and one-company factory** — canonical [TASK-06C-04](../tasks/TASK-06C-04-company-context.md).
- [ ] **06c.5 Fan-out reads** — canonical [TASK-06C-05](../tasks/TASK-06C-05-fanout-reads.md).
- [ ] **06c.6 Provisioning lifecycle** — canonical [TASK-06C-06](../tasks/TASK-06C-06-provisioning.md).
- [ ] **06c.7 Deactivation** — canonical [TASK-06C-07](../tasks/TASK-06C-07-deactivation.md).
- [ ] **06c.8 Migration fan-out and serving guard** — canonical [TASK-06C-08](../tasks/TASK-06C-08-migration-fanout.md).
- [ ] **06c.9 `company_id` retrofit** — canonical [TASK-06C-09](../tasks/TASK-06C-09-company-id-retrofit.md).
- [ ] **06c.10 Backup and sync boundaries** — canonical [TASK-06C-10](../tasks/TASK-06C-10-backup-sync.md).
- [ ] **06c.11 Companies API and permissions** — canonical [TASK-06C-11](../tasks/TASK-06C-11-companies-api.md).
- [ ] **06c.12 Seed and acceptance verification** — canonical [TASK-06C-12](../tasks/TASK-06C-12-acceptance.md).
- [ ] **06c.13 Specialist review** — canonical [TASK-06C-13](../tasks/TASK-06C-13-specialist-review.md).
- [ ] **06c.14 Final verification and documentation** — canonical [TASK-06C-14](../tasks/TASK-06C-14-stage-closure.md).

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 06C-01 | DOMAIN/INFRASTRUCTURE/DATABASE | Registry foundation | — | READY |
| 06C-02 | DOMAIN/INFRASTRUCTURE/DATABASE | Registry saga records | 06C-01 | NOT_STARTED |
| 06C-03 | APPLICATION/INFRASTRUCTURE/SECURITY | Company routing | 06C-01 | NOT_STARTED |
| 06C-04 | APPLICATION/INFRASTRUCTURE/SECURITY/TEST | Company context | 06C-03 | NOT_STARTED |
| 06C-05 | APPLICATION/INTEGRATION/TEST | Bounded fan-out reads | 06C-04 | NOT_STARTED |
| 06C-06 | APPLICATION/INFRASTRUCTURE/DATABASE/SECURITY | Resumable provisioning | 06C-02, 06C-04 | NOT_STARTED |
| 06C-07 | APPLICATION/API/SECURITY/TEST | Deactivation | 06C-06 | NOT_STARTED |
| 06C-08 | INFRASTRUCTURE/DATABASE/APPLICATION/TEST | Migration fan-out | 06C-01, 06C-03, 06C-06 | NOT_STARTED |
| 06C-09 | DOMAIN/INFRASTRUCTURE/DATABASE/TEST | Company identity retrofit | 06C-04, 06C-08 | NOT_STARTED |
| 06C-10 | INFRASTRUCTURE/INTEGRATION/SECURITY/TEST | Per-database backup/sync | 06C-02, 06C-04, 06C-09 | NOT_STARTED |
| 06C-11 | API/APPLICATION/SECURITY/TEST | Companies API | 06C-06, 06C-07, 06C-08 | NOT_STARTED |
| 06C-12 | TEST/DOCUMENTATION | Acceptance evidence | 06C-01…06C-11 | NOT_STARTED |
| 06C-13 | REVIEW | Specialist review | 06C-12 | NOT_STARTED |
| 06C-14 | STAGE-CLOSURE/DOCUMENTATION/REVIEW | Stage closure | 06C-13 | NOT_STARTED |

Only 06C-01 is READY; parallel execution is not authorized.

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
