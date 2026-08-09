# DATA MODEL — Zenith Retail

The rules every table in Zenith obeys, and the register of what exists so far. Written in Stage 01
and extended by every stage that adds a schema.

`CONVENTIONS.md` §2 covers naming. This file covers shape, and the reasons.

---

## 1. The mandatory columns

`CLAUDE.md` §7 rule 3. Every table carries these, and no module declares them — they come from
`Entity` in the domain and are mapped once by `EntityConfiguration<T>` in
`src/ZenithRetail.Infrastructure/Persistence/Configurations/`.

| Column | Type | Why |
|---|---|---|
| `id` | `uuid` | UUID v7, generated client-side (ADR-004). An offline terminal creates rows without a round-trip and without collisions, and the time-ordering keeps B-tree locality. |
| `tenant_id` | `uuid not null` | The isolation boundary. A global query filter applies it to every query (R8). |
| `store_id` | `uuid null` | The owning store, or null for tenant-wide rows like the chart of accounts. |
| `created_at` | `timestamptz not null` | UTC, from `IClock`. Never the wall clock. |
| `created_by` | `varchar(128) not null` | `user:{id}`, `terminal:{id}` or `system:{component}`. Stable identifier, never a display name. |
| `updated_at` | `timestamptz not null` | UTC. |
| `updated_by` | `varchar(128) not null` | As `created_by`. |
| `row_version` | `bytea not null` | Optimistic concurrency token, replaced by the persistence layer on every write (ADR-035). |
| `sync_state` | `varchar(32) not null` | Where the row stands in replication (ADR-006). Stored as text, never as an ordinal. |
| `deleted_at` | `timestamptz null` | Soft delete (§7 rule 8). |
| `deleted_by` | `varchar(128) null` | Who deleted it. |

Three indexes come with them on every table: `ix_<table>_tenant_id`,
`ix_<table>_tenant_id_store_id`, and a partial `ix_<table>_sync_state` filtered to
`sync_state <> 'Synced'` — the sync dispatcher's hot query is "what is still pending", and indexing
the synced majority would waste the index.

### What the persistence layer does for you

Written once in `AuditInterceptor`, so no module repeats any of it:

1. `created_*` and `updated_*` are stamped from `IClock` and `IPrincipalAccessor`. Business code
   that sets an audit field is writing an audit trail it controls, which is not an audit trail — and
   the interceptor un-modifies `created_at`/`created_by` on update so a round-tripped entity cannot
   rewrite its own origin.
2. `Remove()` on a tracked entity becomes a soft delete. A caller reaching for `Remove` means "make
   this go away", and the persistence layer is the thing that knows how that is done here.
3. An entity marked `IImmutableRecord` is refused in the `Modified` or `Deleted` state (§7 rule 7,
   ADR-012).
4. `row_version` is replaced.
5. A `platform.audit_entries` row is written, in the same transaction as the change.

Two global query filters are applied to every entity by convention: `deleted_at IS NULL`, and
`tenant_id = <ambient tenant>` unless an explicit `ITenantContext.BypassTenantFilter(reason)` scope
is open. An unresolved tenant matches nothing rather than everything — the activation wizard and the
login screen must not be able to read the whole estate.

---

## 2. Types

| Concept | Storage | Rule |
|---|---|---|
| Money | `numeric(18,4)` + `char(3)` currency, as `<name>_amount` / `<name>_currency` | §7 rule 4. Never a bare decimal. Scale 4 so a calculation does not round mid-way; round once when a document line is finalised, midpoints away from zero (ADR-033). |
| Quantity | `numeric(18,6)` + `varchar(16)` UoM, as `<name>_value` / `<name>_uom` | §7 rule 5. Six places because retail weighs things and BOM explosion multiplies fractions several levels deep. |
| Timestamps | `timestamptz`, UTC | §7 rule 9. Converted to a store's local time at the presentation edge only. |
| Enums | text | An enum persisted by ordinal turns a reordered member into silently relabelled history. |
| Identifiers | `uuid` | UUID v7 (ADR-004). Never `Guid.NewGuid()`, never an identity column. |

Map money and quantity with `builder.HasMoney(...)` / `builder.HasQuantity(...)` from
`ValueObjectMapping`. Do not hand-roll the column pair.

---

## 3. Schemas

Schema name = module name (ADR-010). Declared as constants on
`ZenithRetail.Infrastructure.Persistence.Schemas`, and an architecture test fails the build if a
table lands anywhere else — including `public`, which belongs to no module and therefore to everyone.

| Schema | Owns | Stage |
|---|---|---|
| `platform` | tenants, stores, the audit trail | 01 |
| `identity` | users, roles, permissions, terminals | 02 |
| `sync` | outbox, inbox, cursors, conflict review queue | 04 |
| `licensing` | licences, leases, activations, entitlements, metering | 04b |

**No foreign key crosses a schema boundary.** Modules reference each other by ID and resolve through
published contracts or domain events. This is the constraint that keeps the modular monolith
extractable — a cross-schema foreign key means the database will refuse to let the modules be
separated, whatever the code says. There is an architecture test.

---

## 4. Tables in `platform`

### `platform.tenants`

The isolation root. A tenant's `tenant_id` is its own `id`, so the global query filter treats it like
every other row rather than needing a special case somebody later gets wrong.

`legal_name`, `trading_name`, `locale`, `base_currency`, `time_zone`, `tax_registration_number`,
`is_active`. The `en-ZA` / `ZAR` / `Africa/Johannesburg` defaults from `CLAUDE.md` §9 are seed values
applied at creation, not constants in code — every one is editable. Tax is deliberately absent: it is
a rules engine (ADR-014), built in Stage 07.

Timezones are IANA identifiers (`Africa/Johannesburg`), never UTC offsets. Offsets move with
legislation; the identifier survives it.

### `platform.stores`

A trading location. `code`, `name`, `address`, `currency_override`, `time_zone_override`,
`is_active`. The overrides are null by default and fall back to the tenant, so the common case stays
one place to edit.

`ux_stores_tenant_id_code` is unique on `(tenant_id, code)` and **filtered to `deleted_at IS NULL`**.
Store codes appear on receipts and in document number series, so two live stores sharing one would
produce colliding document numbers — but a soft-deleted store must not reserve its code forever.

### `platform.audit_entries`

Requirement R6, append-only. `entity_type`, `table_name`, `entity_id`, `action`, `principal`,
`terminal_id`, `is_system_action`, `occurred_at`, and `changes` as `jsonb`.

`changes` holds only the columns that actually changed, as `{"column": {"from": …, "to": …}}` — the
whole row on every update would make the audit table dwarf the data it describes within a year of
trading. Inserts record the values the row was created with. The audit columns themselves are
excluded from the diff, because every update touches them and they would bury the one column that
mattered.

`is_system_action` distinguishes a sync receiver or a scheduled job from a person, which is the first
question anybody asks during an investigation.

---

## 5. Replication registry

Every persisted entity declares `[Replicated(scope, conflictPolicy)]` (ADR-007). An architecture test
fails the build on a missing one — an entity whose conflict behaviour nobody chose is an entity that
will lose somebody's data quietly. `ReplicationScope.NodeLocal` is a valid answer; silence is not.

| Entity | Scope | Conflict policy | Why |
|---|---|---|---|
| `Tenant` | CloudToStore | CloudWins | The cloud owns tenant configuration and licensing (R2). A store editing its own tenant record and winning would let a lapsed tenant edit its way out of read-only. |
| `Store` | Bidirectional | CloudWins | Head office creates and configures stores; a store manager legitimately edits trading hours and address. When both edit, head office reconciles. |
| `AuditEntry` | StoreToCloud | AppendOnly | Two nodes writing history must merge, never overwrite. |

Stage 04 turns this registry into the sync protocol and extends `docs/SYNC_AND_BACKUP.md`.

---

## 6. Migrations

`src/ZenithRetail.Infrastructure/Migrations`, history table `platform.__ef_migrations_history`.

Every migration is reversible and its `Down` is **run** in the test suite, not assumed —
`MigrationTests` applies the chain to an empty database, reverses it to nothing, and re-applies it.
A `Down` that was never run is a `Down` that does not work, and the moment it is needed is the moment
an upgrade has gone wrong in a shop on a Saturday.

`MigrationTests.The_model_and_the_migrations_agree` diffs the model against the snapshot, so a model
change without a scaffolded migration fails here rather than on a customer's database.

Scaffold with:

```bash
dotnet ef migrations add <Name> --project src/ZenithRetail.Infrastructure --context ZenithRetailDbContext
```

`Down` does not drop the schema. EF's own history table lives in `platform`, and dropping the schema
would take the bookkeeping with it.
