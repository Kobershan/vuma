# STAGE 01 — Persistence core: EF Core, base entity mapping, migrations, audit

**Status:** DONE (2026-08-09) · **Depends on:** 00 · **Reference reading:** `CLAUDE.md` §7 (rules 1–11), §8, `docs/CONVENTIONS.md` §2 and §6, `docs/DECISIONS.md` ADR-002, ADR-004, ADR-005, ADR-006, ADR-007, ADR-010, ADR-012, `docs/TESTING.md` §2

## Objective

The persistence layer every module from Stage 02 onward writes through. Stage 00 declared the
mandatory columns on `Entity`; this stage maps them to PostgreSQL **once**, in a base configuration,
so no module can map them differently or forget one.

The value of this stage is what it makes impossible. After it, a module author cannot write an
un-audited row, cannot see another tenant's data by forgetting a `Where`, cannot hard-delete, cannot
call `SaveChanges` from an endpoint, and cannot draw a foreign key across a module boundary. Each of
those is a build break or a failing test, not a review comment.

No business module is built here. The only entities mapped are the platform ones every other schema
depends on — the tenant, the store, and the immutable audit trail (R6).

## Deliverables

**Application ports** (`VumaRetail.Application.Abstractions`)
- `IClock` — the only source of time in the solution. `DateTime.Now` and `DateTimeOffset.UtcNow` are
  banned outside its implementation, so a licensing ladder, a lease expiry or a period close can be
  tested on a virtual clock (`CONVENTIONS.md` §6).
- `IPrincipalAccessor` — who is acting: user id, display name, terminal, and whether this is a system
  principal. The audit interceptor reads it; business code never sets an audit field itself.
- `ITenantContext` — the ambient tenant and store, and a **explicit, auditable** escape hatch
  (`BypassTenantFilter`) for the two legitimate cross-tenant callers: the sync receiver at the cloud
  tier and the backup job.
- `IUnitOfWork` — the transaction boundary the Stage 03 command pipeline drives. Handlers never
  commit for themselves.

**Domain — `platform` module**
- `Tenant` — the licensing and data-isolation root: legal name, trading name, locale, currency,
  timezone, and the `en-ZA` / `ZAR` / `Africa/Johannesburg` defaults as *data* (`CLAUDE.md` §9).
- `Store` — a trading location within a tenant, with its own currency and timezone override.
- `AuditEntry` — append-only. Entity type, key, action, principal, terminal, UTC instant, and the
  changed columns as before/after JSON. Immutable by construction (ADR-012, R6).
- `Replicated` attribute — declares an entity's `ConflictPolicy` and replication tier. Stage 04 reads
  it; declaring it here means the registry cannot be retrofitted onto entities built in between.

**Infrastructure — persistence**
- `VumaRetailDbContext`: schema-per-module (ADR-010), `snake_case` naming applied by convention
  rather than per-property, and configuration discovered from the assembly.
- `EntityConfiguration<T>` base: maps the `CLAUDE.md` §7 rule 3 columns exactly once — `id uuid` PK,
  `tenant_id`, `store_id`, `created_at/by`, `updated_at/by`, `row_version` mapped to PostgreSQL
  `xmin`, `sync_state`, `deleted_at/by`. Every entity configuration derives from it.
- **Global query filters** on every entity: soft delete (§7 rule 8) and tenant isolation, applied by
  convention from the base configuration so a module cannot forget either.
- Value converters: `Money` → `amount decimal(18,4)` + `currency char(3)`, `Quantity` →
  `quantity decimal(18,6)` + `uom`, `DateTimeOffset` → `timestamptz`, enums → their name as text so a
  migration cannot silently reorder them.
- `AuditStampingInterceptor` — stamps `MarkCreated`/`MarkUpdated` from `IPrincipalAccessor` and
  `IClock`, and rewrites a hard `Remove` into a soft delete.
- `AuditTrailInterceptor` — writes the `platform.audit_entries` row for every insert, update and soft
  delete in the same transaction as the change itself.
- `SystemClock`, `SystemPrincipalAccessor`, `AsyncLocalTenantContext` and the
  `AddVumaPersistence(...)` DI extension.

**Migrations**
- `InitialCreate` in `src/VumaRetail.Infrastructure/Migrations`, creating the `platform` schema and
  its three tables. `Down` is written and **tested to actually reverse**, not assumed.
- `scripts/pg-dev.sh` — an ephemeral PostgreSQL cluster on a spare port for tests and migrations,
  using the locally installed server binaries. No Docker, no sudo, no shared state.

**Tests**
- `tests/VumaRetail.IntegrationTests` — a real PostgreSQL through a fixture, real migrations, per-test
  template-database cloning so tests are isolated and fast (`docs/TESTING.md` §2 and `CONVENTIONS.md` §7).
- Two new architecture rules, added now because both become unenforceable once forty modules exist:
  **no `SaveChanges` outside the persistence layer** (§7 rule 2) and **no cross-schema foreign keys**
  (`CONVENTIONS.md` §2).
- A third rule guarding the new `IClock`: **no `DateTime.Now` / `DateTimeOffset.UtcNow` outside
  `SystemClock`**.

**Docs and CI**
- `docs/DATA_MODEL.md` — the mandatory columns, the schema list, the naming rules and the
  replication registry that Stage 04 extends.
- CI `migrate-check` stops being a no-op: it applies every migration to an empty database, reverses
  the last one, and re-applies it.

## Business rules

- **A row's audit fields are written by the persistence layer, never by business code.** An entity
  that stamps its own `UpdatedBy` is writing an audit trail it controls, which is not an audit trail.
- **Nothing is hard-deleted** (§7 rule 8). A `Remove` on a tracked entity is rewritten as a soft
  delete rather than throwing, because a module author calling `Remove` means "make it go away" and
  the persistence layer knows how that is done here.
- **Tenant isolation is a filter, not a habit.** Every query is tenant-filtered by default. The two
  callers that legitimately cross tenants must say so explicitly and are logged when they do.
- **Audit entries are append-only.** No update path and no delete path exists for them, and the
  entity has no mutating members. Attempting either fails at compile time.
- **All timestamps are `timestamptz` in UTC** (§7 rule 9). Conversion to a store's local time happens
  in the presentation edge only.
- **Concurrency is optimistic on `xmin`.** A second writer to a row that changed under them gets a
  `DbUpdateConcurrencyException`, not a lost update. This matters more here than in a normal app
  because sync replays writes.
- **No cross-schema foreign keys** (`CONVENTIONS.md` §2). Modules reference each other by ID. This is
  the constraint that keeps the modular monolith extractable (ADR-010).

## Tests / acceptance

- Every `Entity` column exists in the database with the right PostgreSQL type, checked by reading
  `information_schema` rather than by trusting the model
- `RowVersion` maps to `xmin`: two contexts loading the same row, both writing, and the second
  failing with a concurrency exception
- Soft delete: a deleted row disappears from a normal query, is still present with
  `IgnoreQueryFilters()`, and still physically exists in the table
- `Remove` on a tracked entity produces a soft delete, not a `DELETE`
- Tenant filter: two tenants' rows in one table, a query under tenant A returns only A's;
  `BypassTenantFilter` returns both
- Audit stamping: insert stamps created and updated from the clock and principal; update leaves
  `CreatedAt` untouched; the clock is the fixed test clock, proving nothing reads the wall clock
- Audit trail: an insert, an update and a soft delete each produce exactly one `audit_entries` row
  with the right action and the changed columns; a rolled-back transaction produces none
- `Money` round-trips through the database at scale 4 with its currency; `Quantity` at scale 6
- Migration: `Up` on an empty database, `Down` reverses it to nothing, `Up` again succeeds
- Architecture: `SaveChanges` outside the persistence layer fails; a cross-schema foreign key fails;
  `DateTimeOffset.UtcNow` outside `SystemClock` fails. Each proven by a deliberate violation.

## Exit checklist

- [x] `dotnet build -c Release` — **0 warnings, 0 errors** solution-wide
- [x] `dotnet test` — **127 passed, 0 failed** (78 unit, 15 architecture, 34 integration), stable
      across three consecutive runs
- [x] Domain + Application line coverage **94.9%** (Domain 94.8%, Application 100%) — the §8 bar is 80%
- [x] Base entity columns mapped once, in `EntityConfiguration<T>`, asserted against
      `information_schema` for every table
- [x] Soft-delete and tenant global query filters applied by convention, both proven — including
      that an unresolved tenant sees nothing and that the physical row survives a `Remove`
- [x] Audit stamping and the immutable audit trail proven on a controlled clock, including that a
      rolled-back transaction leaves no entry and that a detached entity cannot rewrite its origin
- [x] `InitialCreate` applies, reverses and re-applies — in the test suite and in CI
- [x] Four new architecture tests, each **proven to fail on a deliberate violation** (see below)
- [x] `docs/DATA_MODEL.md` written; CI `migrate-check` runs up → down → up against real PostgreSQL
- [x] `docs/PROGRESS.md` updated, ADR-035 and ADR-036 appended, committed

### Enforcement proven, not assumed

Each new rule was broken on purpose and the break confirmed before reverting:

| Rule | Violation introduced | Reported as |
|---|---|---|
| No wall clock outside `SystemClock` | `DateTimeOffset.UtcNow` in Infrastructure | `DeliberateViolation.cs:5 — public static DateTimeOffset Now() => DateTimeOffset.UtcNow;` |
| No `SaveChanges` outside persistence | `context.SaveChanges()` in Infrastructure | `DeliberateViolation.cs:6 — public static void Save(DbContext context) => context.SaveChanges();` |
| No cross-schema foreign key | an `identity` entity with an FK to `platform.stores` | `identity.deliberate_terminals → platform.stores` |
| Every entity declares replication | that entity, with no `[Replicated]` | `VumaRetail.Domain.Identity.DeliberateTerminal` |

The wall-clock rule also caught a genuine read on its first run — `UuidV7.NewGuid()`. That one is
exempted with the reason stated at the exemption: a UUID v7 is time-ordered by construction (ADR-004)
and Domain references nothing (§7 rule 1), so it cannot take an `IClock`; the timestamp inside a key
is an ordering device that no business rule reads, and `NewGuid(DateTimeOffset)` exists for tests.

### Corrections to earlier work

`UuidV7Tests.Encodes_the_supplied_timestamp_and_reads_it_back` (Stage 00) was order-dependent and
started failing once this stage added tests that generate IDs at the current time. `UuidV7` keeps a
process-wide monotonic high-water mark so a backwards clock cannot issue an out-of-order key, which
makes the encoded timestamp `max(supplied, high-water)` — so a fixed literal passed or failed
depending on what had already run in the assembly. The production behaviour is correct and was left
alone; the test now derives its instant from the current high-water mark.

### Deviations from the plan, and why

- **`row_version` is an application-generated `bytea`, not PostgreSQL's `xmin`.** `docs/PROGRESS.md`
  §5 proposed `xmin`; `CLAUDE.md` §7 rule 3 says `bytea`, and the contract wins. The deciding reason
  is ADR-003: the same rows are cached in SQLite on every terminal, SQLite has no `xmin`, and sync
  compares tokens across both tiers. ADR-035.
- **Integration tests resolve PostgreSQL from a connection string before trying Testcontainers.**
  `TESTING.md` §2 names Testcontainers, which is still preferred when Docker is present — but Docker
  is not installed on the current build machine (`PROGRESS.md` §4.3) and the alternative was leaving
  the stage unverifiable. ADR-036.

## Explicitly deferred

- **`Money`/`Quantity` on a real table.** The mapping helpers exist and are proven against real
  PostgreSQL through a probe entity, but no business entity carries either until Stage 07. Mapping
  them now, against a table nothing uses, would be inventing schema to satisfy a checklist.
- **Sync columns beyond `sync_state`.** The HLC stamp, outbox and inbox are Stage 04. What Stage 01
  owes them is the `[Replicated]` registry, which is complete and enforced.
- **A real `IPrincipalAccessor`.** Stage 02 replaces `SystemPrincipalAccessor` with one backed by the
  authenticated identity. The registration uses try-add semantics so that is a registration, not an
  edit to this stage's code.
