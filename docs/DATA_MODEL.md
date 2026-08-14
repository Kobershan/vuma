# DATA MODEL — Vuma Retail

The rules every table in Vuma obeys, and the register of what exists so far. Written in Stage 01
and extended by every stage that adds a schema.

`CONVENTIONS.md` §2 covers naming. This file covers shape, and the reasons.

---

## 1. The mandatory columns

`CLAUDE.md` §7 rule 3. Every table carries these, and no module declares them — they come from
`Entity` in the domain and are mapped once by `EntityConfiguration<T>` in
`src/VumaRetail.Infrastructure/Persistence/Configurations/`.

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
`VumaRetail.Infrastructure.Persistence.Schemas`, and an architecture test fails the build if a
table lands anywhere else — including `public`, which belongs to no module and therefore to everyone.

| Schema | Owns | Stage |
|---|---|---|
| `platform` | tenants, stores, the audit trail | 01 |
| `identity` | users, roles, grants, role assignments, terminals, refresh tokens | 02 |
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

## 4b. Tables in `identity`

Built in Stage 02. Permissions themselves are **not** a table: they are declared in code by the
module that owns them and assembled into a catalogue at startup (ADR-013). What is stored is which
role was granted which permission string, and granting one no module declares is refused.

No foreign key leaves this schema. `Terminal.StoreId` points at `platform.stores` by ID only, because
a foreign key there would cross a module boundary (§3).

### `identity.users`

`user_name`, `normalized_user_name`, `display_name`, `email`, `normalized_email`, `password_hash`,
`security_stamp`, `pin_hash`, `failed_password_attempts`, `password_locked_until`,
`failed_pin_attempts`, `pin_locked_until`, `last_signed_in_at`, `is_active`.

Two credentials, locking independently. The password is for the back office and the API; the 4–8
digit PIN identifies an operator at a till that has already authenticated with its certificate. They
lock separately so a shift of mistyped PINs cannot lock a manager out of the reports, and a
password-guessing attempt cannot stop the till trading — which would breach R1 through the back door.

`ux_users_tenant_id_normalized_user_name` is unique and filtered to `deleted_at IS NULL`, so a
cashier who leaves and returns keeps their own name. `ix_users_tenant_id_pin` is partial on
`pin_hash IS NOT NULL AND deleted_at IS NULL` — the predicate the PIN sign-in query actually uses.

The security stamp changes whenever a credential changes, which retires every token issued before it
without having to find them (ADR-038).

### `identity.roles` and `identity.role_permissions`

`name`, `normalized_name`, `description`, `is_system_role`; and `role_id` + `permission`.

A row per grant rather than a collection on the role: adding one permission to a role with forty
writes one row, syncs one row, and audits as one change naming the permission that changed — which is
the question asked afterwards. `is_system_role` marks the roles the seed owns; they may be renamed
but not removed, because deleting the one role holding `identity.role.assign` locks a tenant out of
their own installation with no way back in.

### `identity.user_role_assignments`

`user_id`, `role_id`, and the base entity's own `store_id` as the scope — `null` means tenant-wide.
A second scope column would be a column that can disagree with the one every other table already
uses.

`ux_user_role_assignments_user_id_role_id_store_id` is unique with **`NULLS NOT DISTINCT`**
(PostgreSQL 15+). Under the SQL default two nulls are distinct, so the same tenant-wide role could
otherwise be assigned to one user any number of times.

### `identity.terminals`

`code`, `name`, `status`, `enrolment_code_hash`, `enrolment_code_expires_at`,
`certificate_thumbprint`, `device_fingerprint`, `has_fingerprint_drift`, `activated_at`,
`last_seen_at`, `revocation_reason`.

Trust on first enrolment, pinned thereafter. Activation binds the SHA-256 thumbprint and **spends**
the enrolment code — a code that is still comparable after use is a second credential for a terminal
that already has a certificate. `ux_terminals_certificate_thumbprint` is unique across the whole
installation, because a thumbprint is resolved before any tenant is known and a collision would be one
store's till authenticating as another's.

`has_fingerprint_drift` is observed, never enforced (ADR-026): a replaced motherboard on a Saturday
must not close a till.

### `identity.refresh_tokens`

`user_id`, `token_hash`, `security_stamp`, `issued_at`, `expires_at`, `revoked_at`,
`revocation_reason`, `replaced_by_token_id`.

Only the SHA-256 digest is stored — a database dump holding 30-day bearer credentials in plaintext is
a month of unrestricted access to every account in it. Unsalted and deterministic here on purpose: the
token is 256 bits this system generated rather than something a person chose, and the refresh path has
to *find* the row by digest.

Rotation marks the old row replaced rather than deleting it, so a token presented twice is visible.
Both a replay and a theft get the same answer — every live token for that user is revoked.

---

## 4c. Tables in `catalog`

Built in Stage 06. What a tenant sells: units of measure, items, variants and barcodes. No stock
quantity, no price, no GL account — those are Stage 08, Stage 10 and Stage 07 respectively (Stage 06's
own scope note). No foreign key leaves this schema; `Item.UnitOfMeasureId` and `Barcode.ItemId`/
`ItemVariantId` are same-schema plain references, and `Item.TaxClassCode` is a string code rather than
a reference into `finance`, per `CONVENTIONS.md` §2.

### `catalog.units_of_measure`

`code`, `name`, `type`, `base_unit_of_measure_id`, `conversion_factor_to_base`, `is_active`.

A unit either *is* a base unit (`base_unit_of_measure_id` is `null`, factor `1`) or converts to one by
multiplication — one level, never a graph, because no retailer's packaging needs a conversion chain and
a chain needs a path-finding query on every stock movement. `ux_units_of_measure_tenant_id_code` is
unique on `(tenant_id, code)`, filtered to `deleted_at IS NULL`.

### `catalog.items`

`code`, `name`, `description`, `item_type`, `unit_of_measure_id`, `tax_class_code`, `is_active`,
`has_variants`.

`has_variants` is the domain's own record of which of the two states — sells as itself, or sells only
through a variant — the item is in; `Barcode.CreateForItem` reads it before attaching directly to an
item. `ux_items_tenant_id_code` is unique on `(tenant_id, code)`, filtered to `deleted_at IS NULL`, the
same shape as `platform.stores`.

### `catalog.item_variants`

`item_id`, `sku`, `attributes` (`jsonb`), `is_active`.

`attributes` is an ordered list of name/value pairs — `Size: M`, `Colour: Red` — stored as one `jsonb`
column rather than a child table (ADR-054). Created only through `Item.AddVariant`, never directly, so
the parent item is always the one place that records it has gained its first variant.
`ux_item_variants_tenant_id_sku` is unique on `(tenant_id, sku)`, filtered to `deleted_at IS NULL`.

### `catalog.barcodes`

`code`, `symbology`, `item_id`, `item_variant_id`, `is_primary`.

Attaches to exactly one of an item (only while that item has no variants) or a variant — enforced both
by the domain (`Barcode.CreateForItem`/`CreateForVariant`) and by a database check constraint,
`ck_barcodes_exactly_one_owner`. `ux_barcodes_tenant_id_code` is unique **tenant-wide regardless of
symbology** — a scanner reads a code, not a type. Two more unique, partial indexes
(`ux_barcodes_item_id_primary`, `ux_barcodes_item_variant_id_primary`) back the "exactly one primary
per owner" invariant at the database level, belt-and-braces alongside `Barcode.SetPrimary`'s own
bookkeeping, against the one write two terminals could race on. `Barcode` is the one entity in this
module that is hard-deleted rather than deactivated — see ADR-055 for why, and `docs/SYNC_AND_BACKUP.md`
§3 for how that interacts with replication.

## 4d. Tables in `partners`

Built in Stage 06. Who a tenant trades with. No credit terms, no balance, no ledger account — Stage 07
owns AR/AP against a `PartnerId` it doesn't need this schema to widen for.

### `partners.partners`

`code`, `name`, `type`, `address_*` (the same structured `Address` value object `platform.stores` uses,
ADR-037), `email`, `phone`, `tax_number`, `is_active`.

`type` is `PartnerType`, a `[Flags]` enum stored as text — `Customer`, `Supplier`, or both — and the
domain refuses `None` (`PartnerRuleException.MustBeCustomerOrSupplier`): the flag is the reason the
type exists. `ux_partners_tenant_id_code` is unique on `(tenant_id, code)`, filtered to
`deleted_at IS NULL`.

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
| `User` | Bidirectional | CloudWins | Head office creates staff, but a store manager legitimately resets a cashier's PIN on a Saturday with the line down. When both edited, head office reconciles. |
| `Role` | CloudToStore | CloudWins | A permission grant is a security decision. A store server compromised on a shop floor must not be able to grant itself a permission and have that replicate upwards. |
| `RolePermission` | CloudToStore | CloudWins | As `Role`, and for the same reason. |
| `UserRoleAssignment` | CloudToStore | CloudWins | As `Role`. Who holds what is decided centrally. |
| `Terminal` | StoreToCloud | StoreWins | The store enrols its own tills; the cloud observes them, and counts them for licensing. |
| `RefreshToken` | NodeLocal | LastWriterWins | A session credential has no business leaving the node that issued it. Shipping these to the cloud would put every store's live sessions in one place for no operational gain. |
| `UnitOfMeasure` | Bidirectional | CloudWins | A store may add a unit locally; head office reconciles a collision the same way it does for `Store`. |
| `Item` | Bidirectional | CloudWins | A new line may be raised at either tier; the cloud is where a multi-store catalogue stays consistent. |
| `ItemVariant` | Bidirectional | CloudWins | Follows its item. |
| `Barcode` | Bidirectional | CloudWins | Attaching a case-pack or legacy code locally is routine; the cloud settles a genuine collision. The one entity in this stage that is hard-deleted rather than deactivated (ADR-055), which the delete itself replicates like any other change. |
| `Partner` | Bidirectional | CloudWins | Suppliers and customers are onboarded at either tier; head office reconciles a duplicate. |

Stage 04 turns this registry into the sync protocol and extends `docs/SYNC_AND_BACKUP.md`. Stage 06
adds the five rows above; see `docs/SYNC_AND_BACKUP.md` §3 for the same registry with schema and
one-line rationale per entity.

---

## 6. Migrations

`src/VumaRetail.Infrastructure/Migrations`, history table `platform.__ef_migrations_history`.

Every migration is reversible and its `Down` is **run** in the test suite, not assumed —
`MigrationTests` applies the chain to an empty database, reverses it to nothing, and re-applies it.
A `Down` that was never run is a `Down` that does not work, and the moment it is needed is the moment
an upgrade has gone wrong in a shop on a Saturday.

`MigrationTests.The_model_and_the_migrations_agree` diffs the model against the snapshot, so a model
change without a scaffolded migration fails here rather than on a customer's database.

Scaffold with:

```bash
dotnet ef migrations add <Name> --project src/VumaRetail.Infrastructure --context VumaRetailDbContext
```

`Down` does not drop the schema. EF's own history table lives in `platform`, and dropping the schema
would take the bookkeeping with it.
