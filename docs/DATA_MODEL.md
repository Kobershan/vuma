# DATA MODEL — Vuma Retail

The rules every table in Vuma obeys, and the register of what exists so far. Written in Stage 01
and extended by every stage that adds a schema.

`CONVENTIONS.md` §2 covers naming. This file covers shape, and the reasons.

> **How to read this file for a stage.** This file is long (it grows a section every stage) and it is
> reference material, not narrative — you do not need to read it top to bottom. §1-3 (mandatory
> columns, types, schema list) are short and worth reading once. After that, **jump straight to the
> `§4x. Tables in <schema>` section(s) for the module your stage actually touches** — each is
> self-contained — plus §5's replication registry only for the entities your stage replicates. Reading
> all fifteen module sections to work on one is the single most common way a session burns context on
> this file for no reason.

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

## 2b. One database per company (from Stage 06c)

A tenant's data lives in **one database per company**, plus **one registry database** holding what spans
them (ADR-099, `docs/MULTI_COMPANY.md` §1). Every company database carries the same schema — the whole
of §4 below — and the same migration chain. The registry has its own chain and is migrated first.

```
vuma_<tenant>_registry            companies + connections, company groups, credit groups + holds
                                  + exposure ledger, catalogue routing index, group receipts/payments,
                                  saga intents + legs + outbox, group read models
vuma_<tenant>_<company_code>      the whole Vuma schema, once per company
```

Consequences that every table design has to respect:

1. **No foreign key crosses a database**, which means no foreign key between companies. A cross-company
   reference is an id plus the group document that ties them, resolved through the registry.
2. **No transaction crosses a database** (ADR-116). Anything spanning companies is a saga: an immutable
   intent in the registry, idempotent legs keyed by `(intent_id, leg_id)`, compensation by a new
   document.
3. **`company_id` stays on every business row** even though the database already implies it. It costs
   nothing, it makes a restored or exported database self-describing, and it is the key the registry's
   projections are built on.
4. **Group read models are projections, never sources.** They are fed by each company's outbox, they
   carry `AsAt`, and no commit is ever decided from one (ADR-119).

`tenant_id` remains on every row for the same reasons it always was: the cloud tier holds many tenants'
data side by side, and a restored database must be self-describing.

---

## 3. Schemas

Schema name = module name (ADR-010). Declared as constants on
`VumaRetail.Infrastructure.Persistence.Schemas`, and an architecture test fails the build if a
table lands anywhere else — including `public`, which belongs to no module and therefore to everyone.

**Schemas below are the schemas *inside one company database*, except `registry`, which is the whole of
the registry database.**

| Schema | Owns | Stage |
|---|---|---|
| `registry` | **(registry database)** companies and connections, company groups, credit groups, holds and exposure, catalogue routing index, group receipts and payments, saga intents and legs, group read models | 06c, 06d, 07c |
| `platform` | tenants, stores, the audit trail | 01 |
| `identity` | users, roles, grants, role assignments, terminals, refresh tokens | 02 |
| `sync` | outbox, inbox, cursors, conflict review queue | 04 |
| `backup` | the snapshot ledger | 04 |
| `licensing` | licences, leases, activations, entitlements, metering | 04b |
| `catalog` | items, variants, barcodes, units of measure | 06 |
| `partners` | suppliers, customers, and partners who are both | 06 |
| `finance` | chart of accounts, GL, AR, AP, banking, tax, posting rules | 07 |
| `inventory` | stock locations, the stock ledger, balances, transfers, stocktakes, **reservations** | 08, 08c |
| `pos` | till sessions, sales, sale lines, tenders, receipt prints | 09 |
| `sales` | price lists, promotions, returns, price override logs | 10 |
| `imports` | import batches, mappings, rows, templates | 11 |
| `procurement` | requisitions, RFQs, purchase orders, goods receipts, matches, scorecards | 12 |
| `warehouse` | zones, bins, bin stock, putaway, waves, packing, shipments, counts | 13, 13b |
| `orders` | sales orders, allocations, fulfilments, order returns | 14 |
| `fieldsales` | reps, territories, targets, pro formas, pro forma credit notes, performance snapshots | 14b |
| `conversations` | conversation state, turns, bot order drafts (bindings live in the registry) | 22b |

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

## 4e. Tables in `inventory`

Built in Stage 08. How much of a `catalog` item exists, where, and what it cost. Every item/variant
reference below is a plain `uuid` column with an index and **never** a foreign key into `catalog` —
`CONVENTIONS.md` §2 — validated by the application layer instead.

Four of the six tables carry the same `((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1`
check constraint, mirroring `catalog.barcodes`: a row identifies an item (when it has no variants) or
a variant, never both and never neither. The rule also lives in the domain
(`StockItemReference.Validate`); the constraint is the backstop.

### `inventory.stock_locations`

`code`, `name`, `type`, `is_active`. `ux_stock_locations_tenant_id_code` is unique on
`(tenant_id, code)`, filtered to `deleted_at IS NULL`. `store_id` (the base column) is nullable here
and means it: a central warehouse serving several stores belongs to the tenant, not to one store (R8).

### `inventory.stock_ledger_entries`

The append-only ledger (ADR-005). `location_id`, `item_id`/`item_variant_id`, `movement_type`,
`quantity_value`/`quantity_uom`, `unit_cost_amount`/`unit_cost_currency`, `reference_type`,
`reference_id`, `reason_code`, `note`.

`quantity_value` is **signed** — positive in, negative out — so `SUM(quantity_value)` per location and
stock-keeping unit *is* the on-hand quantity, with no case statement. A check constraint refuses zero:
a zero-quantity row is not a movement, and this table has no update path to correct one with.

`ix_stock_ledger_entries_location_id_created_at_id` is `(location_id, created_at DESC, id DESC)` — the
exact pair the keyset cursor compares, so a page boundary is an index seek rather than a sort of
everything the location has ever done. `ix_stock_ledger_entries_reference_id` is partial on
`reference_id IS NOT NULL` and answers "both sides of this transfer" and "every variance this
stocktake posted".

The entity is `IImmutableRecord`, so `AuditInterceptor` refuses it in the `Modified` or `Deleted`
state (§7 rule 7) — the append-only guarantee is structural, not conventional.

### `inventory.stock_balances`

The projection the ledger sums to. `location_id`, `item_id`/`item_variant_id`,
`quantity_on_hand_value`/`_uom`, `average_cost_amount`/`_currency`.

Two unique indexes rather than one, split on which reference is set —
`ux_stock_balances_location_id_item_id` filtered to `item_id IS NOT NULL` and its variant twin —
because PostgreSQL treats NULLs as distinct in a unique index, so a single index over both nullable
columns would not stop two rows for the same `(location, item, NULL)`. That uniqueness is what stops
two concurrent first receipts each opening their own balance row and the projection reporting half the
stock.

Written only by `StockLedgerPoster`, in the same transaction as the ledger row it corresponds to.

### `inventory.stock_transfers`

The document correlating a transfer's two ledger entries. `source_location_id`,
`destination_location_id`, the item/variant pair, `quantity_*`, `unit_cost_*`, `out_entry_id`,
`in_entry_id`, `note`. A check constraint refuses `source_location_id = destination_location_id`.

Its `id` is minted by the caller *before* either ledger entry is posted, so both can carry it as their
`reference_id` — the one entity in the solution using `Entity`'s caller-minted-id constructor
(ADR-071).

### `inventory.stocktake_sessions` / `inventory.stocktake_lines`

A session carries `location_id`, `status` and `finalized_at`.
`ix_stocktake_sessions_open_by_location` is partial on `status = 'Open'`, which keeps "is a count
already running here?" cheap once a location has years of finalized sessions behind it.

A line carries `stocktake_session_id`, the item/variant pair, `system_quantity_*` and
`counted_quantity_*`. `system_quantity` is snapshotted when the count is *recorded*, not when the
session is finalized — stock keeps moving during a count, and comparing a 09:00 count against a 17:00
system quantity would blame the stocktake for every sale in between. One line per stock-keeping unit
per session, enforced by the same split-unique-index pattern as `stock_balances`: a second count of
the same item is a recount of the existing line, never a second row that would double its variance.

---

## 4f. Tables in `finance`

Built in Stage 07. The double-entry accounting spine: a chart of accounts, an immutable general
ledger, the AR and AP sub-ledgers that reconcile to it, a minimal banking slice and tax as data.
Lettered after `inventory` because that section was written first; §3's table is the stage-ordered
index.

Every partner reference below is a bare `uuid` and **never** a foreign key into `partners` —
`CONVENTIONS.md` §2, and the same rule `inventory` follows for `catalog`. Finance does not resolve a
partner to a name; that is a read model `partners` publishes.

**The one rule that shapes this whole schema:** no module outside `finance` may name a GL account
(CLAUDE.md §7 rule 12). Other modules raise an `IFinancialEvent` carrying named amounts, and
`posting_rules` — tenant data, not code — decides which accounts those amounts hit. So there is no
`account_id` column anywhere outside this schema, and `FinanceRulesTests` fails the build if a type
dependency on `Account` appears in one. Stage 08's inventory events were the first real test of that
and needed no account column; see ADR-016 and the `pos.sale.tendered` rule in `DemoSeed`.

### `finance.accounts`

`code`, `name`, `type` (Asset/Liability/Equity/Revenue/Expense), `parent_account_id` for the
hierarchy, `control_account_type`, `currency`, `is_active`.
`ux_accounts_tenant_id_code` is unique on `(tenant_id, code)`, filtered to `deleted_at IS NULL`.

`ix_accounts_control_account_type` is partial on `control_account_type <> 'None'`, because the only
question ever asked of that column is "which accounts must reconcile to a sub-ledger?" — a handful of
rows out of a chart that may run to hundreds. Both the period-close check and the daily variance job
read exactly this index.

`parent_account_id` is a self-reference and the one place a foreign key would be legal inside the
schema; it is deliberately a plain `uuid` anyway, so the hierarchy can be reparented without the
database arguing about ordering.

### `finance.accounting_periods`

`period_start`, `period_end`, `status` (Open/Closed), `closed_at`, `closed_by`. Tenant-wide, not
per-store — one calendar, so `store_id` is null on every row.

`ix_accounting_periods_status` is partial on `status = 'Open'`. Open periods are a permanently small
set no matter how many years of closed ones accumulate, and every posting resolves one.

A close is refused while any control account disagrees with its sub-ledger, so a period's rows are
only ever `Closed` once `reconciliation_variance_flags` has nothing to say about it.

### `finance.journals` / `finance.journal_lines`

The immutable ledger (§7 rule 7). A journal carries `accounting_period_id`, `journal_number`,
`posted_at`, `posted_by`, `source_module`, `source_event_type`, `source_reference`, `narration` and
`reversal_of_journal_id`. Both entities are `IImmutableRecord`, so `AuditInterceptor` refuses them in
`Modified` or `Deleted` — a posted journal is corrected by a reversal that links back through
`reversal_of_journal_id`, never by an edit. `ix_journals_reversal_of_journal_id` is partial on
`IS NOT NULL`, since reversals are the exception.

A line carries `journal_id`, `line_number`, `account_id`, `description`, the five nullable analysis
dimensions (`department_id`, `cost_centre_id`, `project_id`, `channel_id`, `employee_id` — all bare
opaque ids, none resolved by this module; store is the base `store_id` column), and **four money
columns**: `debit_amount`/`debit_currency` and `credit_amount`/`credit_currency`.

Four plain columns rather than two `Money` complex properties, which is the one piece of this schema
that looks wrong until you know why (**ADR-067**). A line carries a debit *or* a credit, so each is
optional on its own, and EF Core 9 cannot configure a complex property as optional at all. The domain
still speaks `Money`: `JournalLine.Debit` and `.Credit` are computed `Money?` accessors over the
pairs, and only the mapping knows about the split.

`ck_journal_lines_exactly_one_side` enforces
`((debit_amount IS NOT NULL)::int + (credit_amount IS NOT NULL)::int) = 1`. The domain refuses the
same thing in `Journal.Post`, but the constraint is the backstop that a restore, a sync apply or a
hand-run SQL fix also has to pass. `ix_journal_lines_account_id` is what the trial balance and every
account-balance query group by.

Balance is **not** a constraint. Debits equalling credits per currency is checked in `Journal.Post`
before insert, because it is a property of a set of rows rather than of one, and a deferred
constraint that fires at commit would report it far from where it was caused.

### `finance.ar_invoices` / `_lines`, `finance.ar_receipts` / `_allocations`

The AR sub-ledger. An invoice carries `partner_id`, `invoice_number`, `invoice_date`, `due_date`,
`currency`, `status` (Draft/Posted/Settled), `journal_id`, and `total_*` plus
`outstanding_balance_*` money pairs.

Unlike a journal an invoice is **not** `IImmutableRecord`, and the distinction is deliberate: its
outstanding balance legitimately falls as receipts allocate. What freezes at posting is its *lines*,
enforced in the entity (`ArInvoice.AddLine` refuses once status leaves Draft), not by the
persistence-layer guard, which is for records with no legitimate mutation at all.

`ix_ar_invoices_partner_id_status` is partial on `status <> 'Settled'`, which is the ageing report's
and the variance check's whole working set — settled invoices are the vast majority of the table and
neither query ever wants them.

A receipt carries `partner_id`, `receipt_number`, `receipt_date`, `amount_*` and `journal_id`; its
allocations carry `ar_receipt_id`, `ar_invoice_id` and `amount_*`. Over-allocation is refused in the
domain (`OverAllocationException`) — allowing it is precisely how a control account silently stops
matching its sub-ledger.

### `finance.ap_invoices` / `_lines`, `finance.ap_payments` / `_allocations`

The mirror of AR, column for column, with `supplier_invoice_number` in place of a tenant-issued
number — the supplier issues it, so it is not drawn from `document_number_counters`. Event types
`ap.invoice.posted` / `ap.payment.posted`.

### `finance.bank_accounts` / `finance.bank_statement_lines`

`ux_bank_accounts_gl_account_id` is unique on `gl_account_id`: one bank account per GL bank control
account, or the variance check has two sub-ledgers claiming one balance.

A statement line carries `bank_account_id`, `transaction_date`, `description`, a **signed**
`amount_*` (positive in, negative out — one column, because a bank statement is a single running
balance), `external_reference`, and `matched_journal_line_id`/`matched_at`/`matched_by`.

`ux_bank_statement_lines_bank_account_id_external_reference` is unique and is what makes importing
the same statement twice a no-op rather than a doubled balance.

**There is no reconciliation-run table.** A line's matched state *is* the reconciliation: the bank
control account's variance check sums the matched lines, so matching a line is the whole act of
reconciling it. `ix_bank_statement_lines_bank_account_id_matched` serves exactly that sum. A separate
run entity would give the reconciled balance two sources, which would eventually disagree.

### `finance.posting_rules` / `finance.posting_rule_lines`

The rule-12 mechanism, as data. A rule carries `event_type`, `description`, `is_active`; a line
carries `posting_rule_id`, `line_number`, `account_id`, `side` (Debit/Credit), `amount_key` — which
named amount on the incoming event it draws from, e.g. `Net`, `Tax`, `Gross` — `inherit_dimensions`
and `description`.

`ix_posting_rules_tenant_id_event_type` is partial on `is_active`, and rules are deactivated rather
than deleted so a journal already posted still shows which rule produced it.

Nothing validates that a rule's lines balance when it is configured — a rule may name any amounts in
any combination. `Journal.Post`'s balance check is the backstop, and a mis-configured rule fails at
posting time with the amounts in hand rather than at configuration time with a guess about them.

### `finance.tax_rules`

`code`, `name`, `rate`, `treatment` (Inclusive/Exclusive), `effective_from`, `effective_to`,
`is_active`. `ix_tax_rules_tenant_id_code_effective_from` answers the only question the engine asks:
which rule applies to this code on this date.

`rate` is a plain `numeric` fraction, **not** a `Money` — it is a multiplier, not an amount, and
routing it through `HasMoney` would attach a meaningless currency to it.

A rate change is a **new row** with its own effective dates, never an edit, so a historical invoice
always recalculates to what it actually charged. The `en-ZA` default seeds exactly one row —
`STANDARD`, 15%, inclusive — and a second jurisdiction is a row, not a release (CLAUDE.md §9).

### `finance.reconciliation_variance_flags`

What the daily job writes: `accounting_period_id`, `account_id`, `control_account_type`,
`gl_balance`, `sub_ledger_balance`, `variance`, `checked_at`.

A row is written on **every** check, including a clean one, so "reconciled today" is distinguishable
from "nobody looked" — an empty table otherwise reads as all-clear whether or not the job ran.
`ix_reconciliation_variance_flags_variance` is partial on `variance <> 0`, which is the only subset
anybody opens a screen to see.

### `finance.document_number_counters`

`series`, `next_value`. `ux_document_number_counters_tenant_id_series` is unique per tenant.

`ReplicationScope.NodeLocal` and **deliberately not replicated** (ADR-065): the number is printed on
the document before any replica could learn of it, so a converging counter would hand two stores the
same invoice number and then agree on which was right. The repository takes a `SELECT ... FOR UPDATE`
row lock inside the *caller's* transaction, so a rejected posting rolls the increment back with
everything else and the series stays gap-free rather than merely unique.

---

## 4g. Tables in `pos`

Built in Stage 09. The till: a cashier's shift, the sales rung up on it, what was paid and how, and
the record of every receipt that came out of the printer.

Every item, variant, customer, terminal and user reference below is a bare `uuid` and **never** a
foreign key into `catalog`, `partners` or `identity` — `CONVENTIONS.md` §2, the same rule `inventory`
follows. Foreign keys *within* this schema are used and correct: a sale line without its sale is
meaningless.

**The rule that shapes this schema is R1 — the till never stops.** Two of the tables below carry
columns that exist only because something downstream is allowed to fail without taking the sale with
it, and those columns are the ones to read first.

### `pos.till_sessions`

`terminal_id`, `operator_user_id`, `currency`, `opening_float_*`, `opened_at`, `status`
(Open/Closed), `counted_cash_amount`, `expected_cash_amount`, `closed_at`, `closed_by_user_id`,
`note`.

`ux_till_sessions_tenant_id_terminal_id_open` is unique on `(tenant_id, terminal_id)` filtered to
`status = 'Open' AND deleted_at IS NULL`. **One open session per terminal is a database guarantee, not
only a check the handler performs** — two concurrent open requests on the same drawer collide here
rather than both succeeding and leaving neither one's expected cash a real number.

The counted and expected amounts are two plain `numeric(18,4)` columns behind computed `Money?`
accessors, per ADR-067; the currency is the session's own. Both are meaningless until the session
closes.

**Nothing writes `expected_cash_amount` from an input.** It is derived at close from the opening float
plus the session's own sales' `CashContribution` — cash tendered less change given, and only on sales
that actually completed. That derivation is the entire control: a cash-up whose expected figure can be
supplied by the person being counted tells you nothing.

### `pos.sales`

`sale_number` (ADR-065's sequence, series `SALE`), `till_session_id`, `terminal_id`,
`operator_user_id`, `location_id`, optional `customer_id`, `currency`, `status`
(Open/Parked/Completed/Voided), the `net_*`/`tax_*`/`gross_*` totals, `amount_tendered_*`,
`change_given_*`, `opened_at`, `completed_at`, `voided_at`, `void_reason`.

`ux_sales_tenant_id_sale_number` is unique per tenant, filtered to `deleted_at IS NULL`.
`ix_sales_till_session_id_status` serves both the cash-up derivation and the check that blocks a close.
`ix_sales_terminal_id_parked` is partial on `status = 'Parked'`, because parked sales are a handful out
of a day's trading and it is the list a cashier opens for every customer who comes back.

Three check constraints restate what the aggregate already enforces, because these are the columns
every later report reads and a null in one of them would be silently wrong rather than loud: a
`Completed` sale has a `completed_at`, a `Voided` one has both `voided_at` and `void_reason`, and
`change_given_amount >= 0`.

**The primary key may be minted by the caller.** UUID v7 is offline-safe by design (ADR-004), so a
terminal that rang a sale up while the network was down replays it under the id it already printed on
the customer's slip, and the replay is idempotent by primary key rather than by a deduplication table
somebody has to remember to check.

### `pos.sale_lines`

`sale_id`, `line_number`, `item_id`/`item_variant_id`, a `description` snapshot taken at ring-up,
`quantity_*`, `unit_price_*`, `discount_*`, `tax_code`, `net_*`, `tax_*`, `gross_*`, `is_voided`,
`voided_at`, and the three stock-issue columns below.

`ux_sale_lines_sale_id_line_number` is unique. Line numbers are never reused after a void: a receipt
whose line 2 is a different item on the reprint than it was on the original is worse than one with a
gap.

The description is snapshotted rather than joined. Renaming the item next month must not rewrite what
a customer was handed.

`ck_sale_lines_balances` asserts `net_amount + tax_amount = gross_amount`, and
`ck_sale_lines_quantity_positive` asserts a positive quantity — a return is a Stage 10 document that
references this sale, not a negative line on it, and a later stage adding returns has to make a
deliberate decision about this table rather than sliding one in.

**`stock_issue` (Pending/Posted/Refused), `stock_ledger_entry_id` and `stock_issue_note` are ADR-073.**
A sale completes even when Stage 08's ledger refuses to relieve the stock behind a line — the customer
is holding the item, and Stage 08 rule 4 still forbids a negative balance, so the sale stands and the
line says what happened. `ix_sale_lines_stock_issue_refused` is partial on `Refused` and is the
reconciliation queue a manager works through. If it ever grows large, that is the signal that the
shelf and the system have drifted apart, not that the index is wrong.

### `pos.sale_tenders`

`sale_id`, `type` (Cash/Card/Voucher/MobileMoney/CustomerAccount), `amount_*`, optional `reference`,
`captured_at`. `IImmutableRecord`, so `AuditInterceptor` refuses the row in `Modified` or `Deleted` —
money that changed hands is corrected by another tender, never by an edit, the same relationship a
journal has with its reversal. `ck_sale_tenders_amount_positive` holds the sign.

Only `Cash` counts towards the drawer, and only cash can produce change; a card overpayment asking for
the difference back is a cash advance, which a till may not do. `CustomerAccount` is declared here and
settled nowhere — ADR-055 gave money held on behalf of a customer its own module at Stage 10b.

### `pos.receipt_prints`

`sale_id`, `printed_by_user_id`, `terminal_id`, `is_reprint`, `reason`, `printed_at`. Append-only and
`IImmutableRecord`.

Reprinting a receipt is the oldest till fraud there is — a second copy of a real sale is what a refund
scheme is built on — so R6's "who did what" covers printing even though printing changes nothing.
`ck_receipt_prints_reprint_has_reason` makes the reason mandatory on a reprint, and **whether a print
is a reprint is derived from the row count rather than supplied by the caller**: a caller who could
declare "this is the first print" could reprint forever without any of them being marked, which would
make the log worse than useless because it would look complete.

## 4h. Tables in `sales`

Built in Stage 10. What something should cost, the specials that change it, what came back over the
counter, and every time somebody sold at a price the resolver did not give them.

Every item, variant, customer, sale, sale-line, user and ledger-entry reference below is a bare `uuid`
and **never** a foreign key into `catalog`, `partners`, `pos`, `identity` or `inventory` —
`CONVENTIONS.md` §2, the same rule `pos` and `inventory` follow. Foreign keys *within* this schema are
used and correct: a price without its list, or a return line without its return, is meaningless.

**Its own schema rather than a corner of `pos`**, even though the till is the loudest caller of both. A
price list is not a till concept — a quote, a lay-by, an ecommerce basket and a wholesale invoice are
all priced off these rows, and a shop with no cash drawer still has prices. Folding them into `pos`
would make every one of those later modules depend on the till.

### `sales.price_lists`

`code`, `name`, `currency`, `kind` (Retail/Wholesale/Staff), `prices_include_tax`, `priority`,
`effective_from`, `effective_to`, `is_active`, optional `store_id`.

`ux_price_lists_tenant_id_code` is unique per tenant. `ix_price_lists_store_id_effective_active` is
partial on the active set and is what the resolver seeks on once per scanned line.

`prices_include_tax` is on the **list**, not the line, because it is a property of how a list was
authored: a South African shelf price is quoted inclusive and a wholesale list is not. Storing it here
means the resolver hands `ITaxCalculator` a stated amount and lets the matched tax rule decide the
split, rather than every caller having to know which kind of list it read.

A `store_id` scopes a list to one branch, and **a scoped list beats a tenant-wide one whatever their
priorities say**. A branch running its own prices set them for a local reason; head office raising the
priority of a national list should not silently override that. Priority breaks ties within a scope, and
`code` breaks ties on priority, so resolution is total rather than dependent on row order.

Deactivated, never deleted (§7 rule 8) — a completed sale has to stay explicable next year.

### `sales.price_list_lines`

`price_list_id`, `item_id` **or** `item_variant_id` (exactly one, `ck_price_list_lines_exactly_one_sku`),
`unit_price_*`, `minimum_quantity`.

**A quantity break is a row rather than a feature.** "R12.99 each, R11.50 from a dozen" is two lines for
the same item at minimum quantities of 1 and 12, so adding a break is data entry and the resolver needs
one rule — take the largest break at or below the quantity being sold — instead of a pricing DSL.

`ux_price_list_lines_list_id_sku_minimum_quantity` is what makes Stage 11's bulk price import
idempotent: re-importing the same sheet updates rows rather than accumulating a second price for the
same break. Two prices for one break is not a price, and the resolver would have to pick one
arbitrarily.

### `sales.promotions`

`code`, `name`, `kind`, `discount_percentage`, `reward_amount` / `reward_currency`,
`required_quantity`, `free_quantity`, `effective_from`, `effective_to`, `days`, `starts_at`, `ends_at`,
`priority`, `is_exclusive`, `is_active`, optional `store_id`.

**A promotion is configuration, not code.** Running "3 for R50 on Fridays" is a row here and a row in
`promotion_lines`; it is never a deployment. That is the whole point of the stage — a shop changes its
specials weekly and cannot wait for a release to do it.

The reward parameters are flat nullable columns rather than a polymorphic hierarchy or a JSON blob.
Five kinds cover what a South African shop actually runs, the set changes rarely, and a flat row is
queryable, diffable and importable in bulk. What keeps the flatness honest is that a kind can only be
*created* with the parameters it needs — `Promotion` has no public constructor, only five factories,
each of which validates its own shape. `ck_promotions_quantities_positive` and
`ck_promotions_percentage_range` re-assert the ranges at the boundary.

`reward_amount` / `reward_currency` is ADR-067's pair: EF Core 9 cannot map an optional complex
property, so an optional `Money` is two columns behind a computed accessor.
`ck_promotions_reward_currency_pairs` asserts they move together — an amount with no currency is the
exact bug §7 rule 4 exists to prevent, and it is invisible until something tries to add it up.

`days` is a flags enum stored as its **integer**, unlike every other enum in this schema. It is a set
rather than a member, and the string form of a combination ("Monday, Wednesday, Friday") is neither
queryable nor stable across a member rename.

A time window that wraps past midnight (22:00–02:00) runs through the small hours rather than reading
as an empty set. A late-night garage forecourt is a real shop.

### `sales.promotion_lines`

`promotion_id`, and exactly one of `item_id`, `item_variant_id` or `category_code`
(`ck_promotion_lines_exactly_one_target`).

**A promotion with no lines applies to everything** — that is how a store-wide clearance is expressed,
and it is why `is_exclusive` exists.

### `sales.sales_returns`

`sale_id`, `return_number` (ADR-065's sequence, series `RTN`), `location_id`, `customer_id`,
`currency`, `reason`, `refund_tender_type`, `authorised_by_user_id`, `status`
(Draft/Completed/Cancelled), `net_*`, `tax_*`, `gross_*`, `raised_at`, `completed_at`, `cancelled_at`.

**A return is a new document, never an edit.** §7 rule 7 says a completed financial document is
amended by a new document, and Stage 09 made that structural rather than advisory:
`ck_sale_lines_quantity_positive` is on `pos.sale_lines` precisely so nobody could later slide a
negative line into a sale a customer is holding a receipt for. This table is the other half of that
decision, and the original sale is read and never written.

`ck_sales_returns_balances` asserts `net + tax = gross`, and
`ck_sales_returns_amounts_not_negative` asserts a refund does not take money off the customer. Both
hold in the aggregate too; they are here because every later report reads these three columns and a
wrong one would be silently wrong rather than loud.

### `sales.sales_return_lines`

`sales_return_id`, `sale_line_id`, `item_id` **or** `item_variant_id`, `description`, `quantity_*`,
`original_quantity_*`, `previously_returned_quantity`, `unit_price_*`, `tax_code`, `net_*`, `tax_*`,
`gross_*`, `original_stock_ledger_entry_id`, `stock_return`, `stock_ledger_entry_id`,
`stock_return_note`.

**`ck_sales_return_lines_within_quantity_sold` is business rule 5 as a database guarantee.** The two
snapshot columns are what make it expressible on the row: `quantity + previously_returned <=
original_quantity`, so a return line written around the aggregate — by an import, a repair script, a
future bug — still cannot claim more than was sold. What the constraint cannot settle is two returns
raced against the same line at the same instant, because each snapshot is honest at the moment it is
taken; that case is settled by the aggregate reading the committed sum inside the command's
transaction. `ux_sales_return_lines_return_id_sale_line_id` stops the other way past a per-row check —
two rows for one original line, each passing on its own.

**The money comes from the original line and its tax is derived, never recomputed** (ADR-075). The
shares are cumulative rather than per-return: each amount is the rounded share of everything returned
so far less the rounded share of everything returned before, so partial refunds telescope and their sum
is exactly the original line. Rounding each return independently does not have that property, and the
failure is a refund of money the shop never took.

`original_stock_ledger_entry_id` is snapshotted because the *cost* is on the issue entry, not on the
sale line — Stage 09 stored what the customer paid, which is right for a receipt and useless for a
stock receipt. A null there is not a fault: it means the original sale completed without relieving
stock (ADR-073), and the return has no cost basis to receive against. `stock_return` is ADR-073's
shape for goods moving the other way, and `ix_sales_return_lines_stock_return_refused` is its
reconciliation queue.

### `sales.price_override_logs`

`sale_id`, `sale_line_id`, `item_id` **or** `item_variant_id`, `operator_user_id`, `quantity_*`,
`resolved_unit_price_*`, `actual_unit_price_*`, `reason`, `occurred_at`. Append-only and
`IImmutableRecord` — a log a later write can overwrite is not a log, the same call `pos.receipt_prints`
made.

Selling off-price is legitimate and routine — a damaged tin, a price match, a manager's goodwill — and
it is also the oldest shrinkage pattern in retail. Both facts are handled by the same thing: the till
does not refuse the override, it records it, so a variance that is one cashier and one item every
Friday is visible as a pattern rather than invisible as a series of individually reasonable decisions.
`ix_price_override_logs_operator_user_id_occurred_at` leads on the operator because that is who the
question is almost always asked about.

The variance is **not stored**: it is a difference between two columns that are already here, and a
stored copy is one more thing that can disagree with them.

`sale_id` and `sale_line_id` are optional because an override is recorded at the point of override,
which may be before the line exists or before the sale completes at all. An override on a sale later
voided is still an override that happened.

---

## 4i. Tables in `imports`

Built in Stage 11. One uploaded file, what it was read as, what it was mapped to, what each of its rows
would do, and — the part rollback is built on — what each row overwrote.

Every item, variant, partner, price-list, location and ledger-entry reference below is a bare `uuid`
and **never** a foreign key out of this schema (`CONVENTIONS.md` §2). Foreign keys *within* `imports`
are used and correct: a row without its batch, or a mapping without its batch, is meaningless.

**Everything here is written before anything else is.** The whole schema exists so that upload, parse,
map and validate have somewhere to happen that is not a business table — which is what makes the
preview an honest preview rather than a report on damage already done. See `docs/IMPORT_PIPELINE.md`.

### `imports.import_batches`

`batch_number`, `target_kind`, `source_format`, `file_name`, `content_hash`, `size_bytes`, `status`,
`duplicate_strategy`, `worksheet`, `source_columns` (`jsonb`), the six row counters, and the who/when
of commit and rollback.

`ux_import_batches_tenant_id_batch_number` is unique per tenant; the number comes from ADR-065's
sequence, series `IMP`.

`ix_import_batches_tenant_id_content_hash_committed` is **partial on committed batches only** and is
the duplicate-file check, run once per upload. `content_hash` is the SHA-256 of the uploaded bytes, and
matching on **content rather than on file name** is the point — the same sheet saved under two names is
the same sheet, and re-importing it is how somebody doubles their opening stock. The index is partial
because uploading the same file twice is fine while the first attempt is still a preview; refusing it
would strand somebody who discarded a batch and started again.

`source_columns` is a `jsonb` array rather than a child table because **order is the whole content**. A
header row is a sequence, and a child table would need a sequence column to say so.

The six counters (`total_rows`, `valid_rows`, `invalid_rows`, `created_rows`, `updated_rows`,
`skipped_rows`) are **recomputed from the rows on every transition**, never incremented. A counter that
is incremented is a counter that drifts, and this one is what a person reads before deciding to commit.

The uploaded bytes are **not** a column here. A 40 MB workbook does not belong in a table a list
endpoint pages over, and the bytes have a different lifetime from the record of what was imported —
they are dropped on commit, while the batch and its rows are kept.

### `imports.import_column_mappings`

`import_batch_id`, `target_field`, `source_column` (nullable), `default_value` (nullable).

`ix_import_column_mappings_batch_id_target_field` is unique — a target field bound twice is a mapping
that cannot be applied, and it is refused at the point the mapping is set rather than discovered per
row.

`source_column` is nullable because of `default_value`: a **constant** for a column the file does not
have at all — a supplier file with no `currency` column, for a tenant that trades in one currency. That
is also why a saved template beats alias matching, which by definition cannot bind a column that is not
there. A binding with neither is refused (`IMPORTS_MAPPING_BINDS_NOTHING`).

### `imports.import_rows`

The single most important table in the module. `import_batch_id`, `row_number`, `raw_values`
(`jsonb`), `normalised_values` (`jsonb`), `status`, `errors` (`jsonb`), `outcome`, `target_entity_id`,
`compensation_entity_id`, and `before_image` (`jsonb`).

`ix_import_rows_batch_id_row_number` is the preview's keyset order. `ix_import_rows_batch_id_status_row_number`
is what the filtered preview seeks on — nobody scrolls forty thousand rows looking for the eleven bad
ones.

`row_number` is the **file's line number, header counted as 1**, so data rows start at 2. A row error
that says "line 400" has to mean the line the person sees in their spreadsheet, not an index.

**Both value maps are kept.** `raw_values` is what the file said, keyed by source header, and it is
what answers "what did the file actually say" a year later. `normalised_values` is the parsed,
canonical form keyed by *target field* — a cell reading `R1 234,56` is stored raw as written and
normalised to `1234.56`. Parsing happens **once**, at validation (`CONVENTIONS.md` §6), and a target
handler reads only the normalised map; a preview and a commit that parse separately are a preview and a
commit that can disagree.

`before_image` is what makes ADR-076's rollback possible: the prior state of the entity this row
updated, as JSON, written by the handler that changed it and read back by nobody else. Each handler
stores **only the fields it can change** — storing the whole entity would break the image the first
time a later stage adds a field, and restoring a field the handler never touched would undo somebody
else's edit.

`outcome` (`Created` / `Updated` / `Movement` / `None`) is what the rollback dispatches on, and
`compensation_entity_id` records the reversing ledger entry for a movement, so the original and its
reversal can be read back as a pair.

`status` carries three *preview* verdicts and three *post-commit* ones. `Valid`, `Invalid` and
`Skipped` are what validation produces — a duplicate is its own answer and not a kind of valid
(ADR-080) — and `Committed`, `Skipped` and `RolledBack` are what the commit and rollback leave behind.

### `imports.import_mapping_templates`

`code`, `name`, `target_kind`, `source_signature`, `bindings` (`jsonb`), `is_active`, `use_count`,
`last_used_at`.

`ux_import_mapping_templates_tenant_id_code` is unique per tenant.
`ix_import_mapping_templates_target_kind_source_signature` is the lookup done once per upload.

`source_signature` is a hash of the **normalised header row**, and it is the mechanism that makes the
second month's import free: a file whose headers match a saved template is mapped on upload with no
human step at all. Normalised on both sides by stripping everything that is not a letter or a digit, so
a supplier who changes `Unit Price` to `unit_price` between months does not silently lose their
template.

Deactivated, never deleted (§7 rule 8) — a batch that was mapped by a template has to stay explicable.

---

## 4j. Tables in `procurement`

Built in Stage 12. Thirteen tables for the five documents buying is made of — a requisition that
commits nothing, an RFQ that asks, an order that commits, a receipt that moves stock, and a match that
decides whether anybody pays — plus the scorecard that is the reason to keep the records at all.

Every item, variant, partner, location, ledger-entry and journal reference below is a bare `uuid` and
**never** a foreign key out of this schema (`CONVENTIONS.md` §2). Foreign keys *within* `procurement`
are used and correct: a line without its document is meaningless.

**No table here names a GL account** (§7 rule 12). Receiving raises Stage 08's valuation event and a
released match raises `procurement.invoice.matched`; Stage 07's rules engine decides the accounts.
`supplier_invoice_matches.journal_id` records *which* journal resulted, which is the audit direction —
not an instruction about what to post. See ADR-081 and ADR-085.

### `procurement.purchase_requisitions` / `_lines`

`requisition_number` (ADR-065's sequence, series `REQ`), `requested_by_user_id`, `location_id`,
`required_by`, `justification`, `status`, and the who/when of raising, submission and the decision —
`decided_by_user_id`, `decided_at`, `rejection_reason` covering approval and rejection in one pair
rather than two.

`ux_purchase_requisitions_tenant_id_number` is unique per tenant and filtered on `deleted_at IS NULL`,
which is the shape every document number in this schema uses. `ix_purchase_requisitions_status_required_by`
is the buyer's work queue: what is approved, ordered by when it is needed.

**There is no `partner_id` on a requisition, deliberately.** A requisition is somebody saying they need
something, before anybody has chosen who to buy it from — and that absence is what lets Stage 15 raise
one from a forecast. A line carries `estimated_unit_cost_*` because the requester may know roughly what
it costs, and nothing is held to it.

`sourced_to_document_id` and `sourced_at` on the line are how a requisition answers "did this ever get
bought?" without the downstream documents having to be searched for it.

### `procurement.rfqs` / `_lines` / `procurement.rfq_responses` / `_lines`

`rfq_number` (series `RFQ`), `title`, the originating `purchase_requisition_id` where there was one,
`closes_at`, `status`, and `awarded_response_id` / `awarded_at`.

A response is one supplier's quote: `partner_id`, `currency`, `quoted_at`, `valid_until`,
`lead_time_days`, `status` and the who/when of the award. `ux_rfq_responses_rfq_id_partner_id` is
unique — one supplier quotes once per RFQ, and a supplier who wants to change their price submits a new
response rather than editing a promise they already made (business rule 2). `ux_rfq_response_lines_response_id_rfq_line_id`
says the same thing one level down.

A response line carries `requested_quantity_*` alongside `quoted_quantity_*` — what was asked for as
well as what the supplier is willing to supply, because a quote for less than the quantity requested is
a normal answer and the difference is the thing a buyer is comparing.

### `procurement.purchase_orders` / `_lines`

`order_number` (series `PO`), `partner_id`, `currency`, delivery `location_id`, `expected_at`,
`status`, `version`, `amends_purchase_order_id`, `rfq_response_id`, and the who/when of approval,
issue, closure and cancellation. `net_*`, `tax_*` and `gross_*` are the sum of the lines.

**`currency` is on the order, not the line** (business rule 4). A line carries `unit_cost_currency`
because `Money` is always paired with its currency, and `ck_purchase_order_lines_currency_matches_order`
is what stops the two disagreeing — §4.13's lesson, that a document letting a currency in through a
line is permanently broken, written as a constraint rather than as a convention.

`ck_purchase_orders_version_positive` and `ix_purchase_orders_amends_purchase_order_id` are the
amendment chain: an issued order is immutable (§7 rule 7), so a change is a new order at `version + 1`
naming the one it supersedes, and the superseded one is cancelled.

A line's `net_*` / `tax_*` / `gross_*` are computed **once, at authoring time**, through
`ITaxCalculator` (ADR-075). Nothing downstream recomputes them — the three-way match compares against
these stored figures, which is what makes the comparison reproducible after a tax rate changes.

`received_quantity_*`, `rejected_quantity_*` and `invoiced_quantity_*` are maintained running totals.
They are the exception to the rule that a running total is not trustworthy, and they are safe here for
the reason `import_batches`' counters are not: they are only ever advanced inside the transaction that
writes the receipt or releases the match, on the one node that owns the order.

### `procurement.goods_receipts` / `_lines`

`receipt_number` (series `GRN`), `purchase_order_id`, `partner_id`, `location_id`, `received_at`, the
supplier's own `delivery_note_number`, `status`, and `received_value_*`.

A line carries `accepted_quantity_*` and `rejected_quantity_*` **separately**, with
`rejection_reason`: only the accepted quantity moves through the ledger and only the accepted quantity
counts as received against the order (business rule 7). `unit_cost_*` is the order's cost, not today's
average — that is what a purchase *is*, and it is the entry that moves the weighted average (ADR-068).

`stock_posting`, `stock_ledger_entry_id` and `stock_posting_note` are ADR-073's surface: a movement the
ledger refuses does not fail the receipt, because the goods are on the dock either way.
`ix_goods_receipt_lines_stock_posting_refused` is **partial on refused rows only** — it exists to serve
`GET /procurement/reconciliation/stock-issues`, which is a short list over a large table, and a full
index on a column that is almost always `Posted` would earn nothing.

`ux_goods_receipt_lines_receipt_id_order_line_id` is unique: one receipt records one line per order
line. Receiving the same order line twice is two receipts, which is what makes cumulative
over-receipt checking (ADR-083) meaningful.

### `procurement.supplier_invoice_matches` / `_lines`

The three-way match. `purchase_order_id`, `partner_id`, the supplier's own `supplier_invoice_number`
and `invoice_date`, the `claimed_net_*` / `claimed_tax_*` / `claimed_gross_*` they are charging,
`status`, `matched_at`, and the who/when of the release.

**The row is written whatever the verdict, including `Blocked`** (ADR-082) — "why did we not pay this
invoice" is a question somebody asks three weeks later.
`ck_supplier_invoice_matches_blocked_not_released` is the database re-asserting that only a `Matched`
or `MatchedWithinTolerance` document may be released.

`price_tolerance_percentage` and `price_tolerance_floor_*` are **snapshotted onto the document**. Tenant
policy changes, and a match that re-judged itself under this month's tolerance would be an audit record
that changes its own story.

A line carries ordered, received, invoiced and `previously_invoiced_quantity_*` side by side — the
last is what makes "nothing is invoiced that was not received" cumulative across several invoices
against one order (business rule 11) — plus the computed `quantity_variance_*` and `price_variance_*`
and a per-line `status`. `ux_supplier_invoice_matches_order_id_invoice_number` stops the same supplier
invoice being matched against the same order twice.

`variances` is `jsonb` on both tables: a variance is a short list of reasons a human reads, and it is
never queried by its contents.

### `procurement.supplier_scorecards`

A frozen snapshot for one supplier over one closed period: `orders_placed`, `lines_ordered`,
`lines_delivered`, `lines_delivered_on_time`, `lines_with_rejections`, the three quantities, the
`price_variance_*` total, and the four derived rates.

`ux_supplier_scorecards_partner_period` is unique, which is the enforcement of ADR-084: **a period may
be snapshotted once.** Recomputing yesterday's supplier rating from today's data restates history, and
a rating that changes when nobody did anything is a rating nobody trusts. The derived rates are stored
rather than computed on read for exactly the same reason.

A period with no orders produces a snapshot of zeroes rather than no row — "we bought nothing from
them" is an answer, and its absence is not.

## 4k. Tables in `warehouse`

Built in Stage 13. Eleven tables subdividing a Stage 08 `inventory.stock_locations` row into zones and
bins, plus the movements and tasks that shelve, count, pick, pack and ship at that granularity.

Every location, item, variant and bin reference below is a bare `uuid` and **never** a foreign key out
of this schema, with one deliberate exception: `bin_id` on `inventory.stock_ledger_entries`, added by
this stage's migration as a plain nullable column (ADR-087) — additive to the table Stage 08 owns, not
a new foreign key relationship, and every historical row simply reads `null` there.

**No table here names a GL account** (§7 rule 12). A cycle count variance raises the same
`InventoryValuationEvent` a Stage 08 stocktake does, through the same `IStockLedgerPoster` — no new
posting rule exists for this module.

### `warehouse.zones` / `warehouse.bins`

`zones`: `location_id`, `code`, `name`, `type` (Receiving/Storage/Picking/Packing/Shipping/Returns),
`is_active`. `ux_zones_location_id_code` is unique per location.

`bins`: `location_id` (denormalised from the zone, the same shape `stock_ledger_entries.location_id`
uses), `zone_id`, `code`, `name`, `type` (Shelf/Pallet/Bulk/Staging/Dock), `capacity_value` /
`capacity_unit_of_measure` (plain nullable columns, not a complex property — EF Core 9 cannot configure
one as optional, ADR-067, the same reason `finance.journal_lines.debit_amount` is a plain column),
`is_active`. `ux_bins_location_id_code` is unique per location. Capacity is informational only — not
enforced (ADR-091, business rule 9).

### `warehouse.bin_stock` / `warehouse.bin_stock_movements`

`bin_stock` is the projection: `bin_id`, item/variant, `quantity_on_hand_*`. Quantity only, no cost —
valuation stays at the location level in `inventory.stock_balances`. Node-local, not replicated
(ADR-069's reasoning applied one level down). `ux_bin_stock_bin_id_item_id` /
`_item_variant_id` are unique per bin.

`bin_stock_movements` is the append-only ledger the projection sums to, at bin granularity:
`bin_id`, item/variant, `movement_type` (PutawayIn/PutawayOut/PickReserve/PickRelease/
InternalTransferIn/InternalTransferOut), signed `quantity`, `reference_type`
(Putaway/Pick/InternalTransfer), `reference_id`. `IImmutableRecord`, the same structural guarantee
`stock_ledger_entries` has. Posted only for movements that redistribute stock **inside** a location —
a movement that changes what the location holds goes through the extended `IStockLedgerPoster`
instead (business rules 2–4).

### `warehouse.putaway_tasks`

One line of unbinned received stock: `location_id`, item/variant, `quantity_*`,
`source_reference_type` (GoodsReceipt/ManualReceipt/Adjustment), `source_reference_id`,
`suggested_bin_id`, `status`, `confirmed_bin_id`, `confirmed_quantity_*`, `confirmed_at`.

May be confirmed more than once — `confirmed_quantity` accumulates and `status` becomes `Confirmed`
only once it equals `quantity`, which is what lets a picker split a task across two bins.
`ix_putaway_tasks_pending_by_location` is partial on `status = 'Pending'`, the same shape
`ix_stocktake_sessions_open_by_location` uses.

### `warehouse.pick_waves` / `warehouse.pick_tasks`

`pick_waves`: `location_id`, `status` (Open/Released/Picked/Packed/Shipped/Cancelled), and the
`released_at` / `picked_at` / `packed_at` / `shipped_at` timestamps of each transition.

`pick_tasks`: `pick_wave_id`, item/variant, `requested_quantity_*`, `outbound_reference` (free text —
Stage 14's `SalesOrderLine.Id` as a string is its real caller now, unchanged shape exactly as this
column's own remarks anticipated), `allocated_bin_id`,
`allocated_quantity_value` / `picked_quantity_value` (plain nullable columns sharing
`requested_quantity`'s own unit of measure, the same `ADR-067` shape `bins.capacity_value` uses),
`status` (Pending/Allocated/Picked/ShortPicked/Cancelled).

### `warehouse.pack_tasks` / `warehouse.shipment_confirmations`

`pack_tasks`: `pick_wave_id` (unique — one pack record per wave), `package_count`, `note`, `packed_at`.

`shipment_confirmations`: `pick_wave_id` (unique), `carrier`, `tracking_number`, `shipped_at`.
`IImmutableRecord` — a correction is a new shipment document, not an edit. Its id is minted before the
document exists (the same `Entity(id, tenantId, storeId)` shape `inventory.stock_transfers` uses) so
the location-level `stock_ledger_entries` row(s) it produces can carry it as their `reference_id`.

### `warehouse.cycle_counts` / `warehouse.cycle_count_lines`

The bin-level mirror of `inventory.stocktake_sessions` / `_lines`: `cycle_counts` carries `location_id`,
an optional `zone_id` narrowing the count, `status`, `scheduled_at`, `finalized_at`.
`ix_cycle_counts_open_by_location` is the same partial-on-open shape stocktakes use.

`cycle_count_lines` carries `bin_id`, item/variant, `system_quantity_*` (a `bin_stock` snapshot taken
when the line is recorded, not read live at finalize — Stage 08's `StocktakeLine` reasoning, applied
one level down) and `counted_quantity_*`. `ux_cycle_count_lines_count_bin_item_id` /
`_item_variant_id` are unique per count per bin, so a recount replaces rather than adds.

**Finalizing a cycle count posts through `inventory`, not only `warehouse`** (business rule 4): a
non-zero line variance calls `IStockLedgerPoster.PostCycleCountVarianceAsync`, which corrects
`inventory.stock_balances` and writes a bin-tagged `inventory.stock_ledger_entries` row, and the same
delta is applied to the line's own `bin_stock` row. A bin-level count that disagrees with the system is
real inventory variance at the location, not a bin reshuffle.

## 4l. Tables in `orders`

Built in Stage 14. Four tables: a promise to fulfil what a customer wants (`sales_orders` /
`sales_order_lines`), and what comes back once some of it shipped (`sales_order_returns` /
`sales_order_return_lines`). Every partner, location, sale and customer-account reference below is a
bare `uuid` and never a foreign key out of this schema (business rule 11); `sales_order_lines` and
`sales_order_return_lines` do carry a real foreign key to their own parent table, the same in-schema
aggregate shape `procurement.purchase_order_lines` uses.

**No reservation ledger of its own, and no table names a GL account** (ADR-092, §7 rule 12).
`sales_order_lines` has no `allocated_quantity` or `fulfilled_quantity` column at all — both are
computed on demand from `warehouse.pick_tasks`, keyed by `outbound_reference = sales_order_lines.id`.
The two genuinely new financial events this stage raises (`orders.order.fulfilled`,
`orders.return.completed`) choose no account; the stock side of a return reuses
`inventory.sale.returned` with no new rule (`StockReferenceType.OrderReturn`'s own remarks, ADR-093).

### `orders.sales_orders` / `orders.sales_order_lines`

`sales_orders`: `order_number` (ADR-065's `ORD` series), `partner_id` (nullable — business rule 10),
`channel` (InStore/Phone/Online/Marketplace — only the first two have a real caller this stage),
`fulfilment_type` (Delivery/ClickAndCollect), `fulfilling_location_id`, `delivery_address_*` (an owned
`Address`, all columns null for click & collect — the same optional-owned-type shape
`sales_orders.delivery_address_*` and `stores.address_*` both use), `status`
(Draft/Confirmed/PartiallyAllocated/Allocated/PartiallyFulfilled/Fulfilled/Cancelled/Closed, always
derived from its lines by `RecomputeStatus`, never set directly outside `Confirm`/`MarkCancelled`),
`payment_status` (Unpaid/Paid/OnAccount — a flag, not a tender of its own), `settling_sale_id` /
`settling_customer_account_id` (both nullable, no foreign key), `currency`, `order_date`,
`requested_fulfilment_date`, `is_revenue_recognised` (guards the one-time-only event, business rule 5),
`cancelled_at` / `cancelled_by` / `cancel_reason`, `net_amount` / `tax_amount` / `gross_amount`.
`ux_sales_orders_tenant_id_order_number` is unique per tenant.

`sales_order_lines`: `sales_order_id` (real FK, in-schema), item/variant, `requested_quantity_*`, the
price snapshot (`unit_price_*`, `discount_amount_*`, `tax_amount_*`, `price_list_id`,
`promotions_summary` — the same shape `sales.price_override_logs` and `sales.sale_lines` already
snapshot pricing onto), `backordered_quantity_*` (the one figure this line keeps of its own — nothing
in `warehouse` can say how much of a line was never even attempted), `line_status`
(Pending/Allocated/PartiallyAllocated/Backordered/Fulfilled/PartiallyFulfilled/Cancelled).

### `orders.sales_order_returns` / `orders.sales_order_return_lines`

`sales_order_returns`: `sales_order_id` (no FK — cross-schema), `return_number` (ADR-065's `ORT`
series, deliberately not `sales.sales_returns`' `RTN` — the two document families must never be
confused in an audit trail, ADR-085's precedent applied to a series, business rule 6),  `reason`,
`authorised_by_user_id`, `status` (Open/Completed), `refund_status` (the same `Unpaid`/`Paid`/
`OnAccount` flag shape as `sales_orders.payment_status`), `net_amount` / `tax_amount` / `gross_amount`,
`raised_at`, `completed_at`. `ux_sales_order_returns_tenant_id_return_number` is unique per tenant.

`sales_order_return_lines`: `sales_order_return_id` (real FK, in-schema), `sales_order_line_id` (no
FK — the parent order line lives in a sibling table of this same schema but the aggregate boundary
stops at the return document), item/variant, `quantity_*` (never more than what was fulfilled less
what earlier returns already took, business rule 6), `fulfilled_quantity_*` (a snapshot of what
`IOrderFulfilmentReader` reported at the moment this line was raised), `previously_returned_quantity`,
the pro-rata refund (`unit_price_*`, `net_amount`, `tax_amount`, `gross_amount` — the same
cumulative-fraction arithmetic `sales.sales_return_lines` documents in full), `stock_return`
(Pending/Posted/Refused — ADR-070/073's precedent, applied to an order return), `stock_ledger_entry_id`,
`stock_return_note`.

---

## 4l. Tables in `registry` — the registry database (Stages 06c, 06d, 07c)

### `registry.companies`
One row per company, and the only place its connection details live. `id`, `tenant_id`, `code`,
`legal_name`, `trading_name`, `registration_number`, `tax_number`, `base_currency`, `locale`,
`document_prefix`, `connection_secret_ref` (**encrypted at rest, never exported, never logged**),
`schema_version`, `migration_state`, `lifecycle_state` (`Provisioning | Seeding | Registered | Active |
Deactivated`), `is_active`. `code` and `document_prefix` unique per tenant. Business operations see
`Active` rows only (ADR-118).

### `registry.company_groups` / `registry.company_group_members`
The sets consolidation and group scope operate over. A company belongs to at most one group.

### `registry.saga_intents` / `registry.saga_legs`
The whole of a cross-company operation, written **before** anything happens. An intent is immutable; a
leg carries `company_id`, `leg_id`, payload, `state` (`Pending | Applied | Failed | Compensated`),
attempt count, `acknowledged_at`, and is idempotent on `(intent_id, leg_id)`. An intent past its timeout
is an alarm with a named owner (ADR-116).

### `registry.credit_groups` / `registry.credit_group_members`
`credit_groups`: `direction` (`Receivable` | `Payable`), `limit_amount` `decimal(18,4)` + currency,
`exposure_policy`, `is_active`. Members carry `company_id`, `partner_id`, optional `sub_limit_amount`.
The group row is the serialisation point — a `row_version` check is not sufficient, the transaction is
**serialisable** (ADR-101).

### `registry.credit_holds` / `registry.credit_exposure_entries`
`credit_holds`: amount, company, document reference, `expires_at`, state. Append-only; a hold that is
never confirmed expires by itself and the credit returns. `credit_exposure_entries`: append-only
confirmed consumption, idempotent per document. **Exposure = confirmed + unexpired holds.**

### `registry.catalog_routing_index`
`(tenant_id, barcode)` → `company_id`, `item_id`, `variant_id`, `pack_size`, `pack_uom_id`, cached
description, `published_at`. Fed by each company's outbox on barcode create/change/retire; rebuildable
by asking every company to republish; tested against the incremental projection (ADR-100).

### `registry.group_receipts` / `registry.group_receipt_allocations`
Captures money once and splits it across companies. **The receipt posts nothing.** Each allocation is a
saga leg keyed by `(group_receipt_id, allocation_id)` that creates a `finance.ar_receipts` row inside
its own company's database; leg state is `Pending → Applied | Compensated`. Σ allocations ≤ captured
amount, as a check constraint and in the aggregate (ADR-104).

### `registry.group_payments` / `registry.group_payment_allocations`
The same shape outbound, for a supplier several companies owe.

### `registry.inter_company_clearing_intents`
Immutable, carrying both legs and the group document that caused them. `Settled` only when both
companies acknowledge. Net-zero is proven by a **scheduled reconciliation across the databases**, not by
a transaction, and a period close refuses over an outstanding intent (ADR-105).

### `registry.operators` (Stage 06e)
The vendor-issued ownership identity, **projected from the signed licence and never created by a tenant
command**: `operator_id` (e.g. `OP-4K2X-9QN7`), display name, licence fingerprint, `is_active`. Every
`registry.companies` row carries `operator_id` (ADR-121).

### `registry.company_links` (Stage 06e)
`company_a_id`, `company_b_id` (stored smaller-GUID-first so a pair has one row), `operator_id`,
`scopes` (`[Flags]`: `SharedFloor, SharedTill, SharedCredit, SharedReceipting, SharedSourcing,
SharedPicking, SharedReporting`), `status` (`Proposed | Accepted | Active | Suspended | Revoked`),
`accepted_by_a`/`_b` + timestamps + licence fingerprint at acceptance, `effective_from`/`_to`,
`revoked_reason`. **Unique on `(company_a_id, company_b_id)`. Check constraint: `operator_id` equals both
companies' `operator_id`** (ADR-121, ADR-122).

### `registry.premises` / `registry.premises_occupancies` / `registry.premises_bin_layouts` (Stage 06e)
The physical site, the companies occupying it (one store each, that store's row living in its own
company's database), and the zone/bin layout mastered at the premises and mirrored into each occupant's
`warehouse` schema. **A quantity never spans companies**; a shelf may hold both companies' goods
(ADR-124).

### `registry.users` / `registry.user_company_access` (Stage 06e)
The user directory: one row per human, one login, owned by an `operator_id`. Access grants roles **per
company**; the JWT carries the companies and the roles in each, and a request naming a company absent
from the token is 403 (ADR-127). A user may only be granted companies under the same Operator ID.

### `registry.terminals` (Stage 06e)
Terminal id, `premises_id`, the companies it may sell for, device certificate thumbprint. Selling for a
sister company requires `SharedTill`, checked per transaction.

### `registry.trading_sessions` / `_segments` / `_lines` / `_tenders` / `_tender_allocations` (Stage 09b)
The mixed basket. One session per customer visit; **one segment per company**, each pricing, taxing and
rounding inside its own company's database; lines carry the resolved `company_id` from the routing index
and a **pack size snapshot**; one tender captured against the session and allocated across segments,
cent-exact. Completion is a saga producing one tax invoice per company, and is idempotent on the
session's `idempotency_key` (ADR-125, ADR-126).

### `registry.contact_bindings` (Stage 22b)
Channel address (E.164 phone or email) → contact → the companies it may reach, with
`verification_state`, `verified_at`, `consent_state`, `locked_until`. **Created tenant-side only** — never
by a requester asserting an identity (ADR-130). OTP challenges are stored hashed with a 10-minute expiry
and a 3-attempt limit.

### `registry.group_availability` / `registry.group_partner_exposure` / `registry.company_period_figures`
The group read models. Each row carries the contributing company and its `AsAt`. Planning inputs only —
no commit is ever decided from one, and a stale contributor is disclosed rather than silently summed
(ADR-119).

---

## 4m. Tables in `inventory`, added by Stage 08c

### `inventory.stock_reservations`
**Inside each company's own database.** Append-only, immutable rows: `company_id`, `location_id`, `item_id`, `variant_id`, `quantity`
`decimal(18,6)` + uom, `source_document_type`, `source_document_id`, `state`
(`Held | Consumed | Released | Expired`), `expires_at`, `reason`. A release or expiry is a **new row**,
never an update — the same shape as the stock ledger (ADR-103).

### `inventory.available_balances`
Projection: `on_hand`, `reserved`, `in_staging`, `available`, `as_at`. Rebuildable from
`stock_ledger_entries` + `stock_reservations` + `warehouse.bin_stock`, and the rebuild must equal the
incremental projection.

---

## 4n. Tables in `warehouse`, added by Stage 13b

### `warehouse.bins` — extended
`bin_type` gains `Consolidation`, `Packing`, `Dispatch`. Stock in one of these is on hand and **not**
available; `StockLocationState` is derived from where the quantity sits, never stored (ADR-114).

### `warehouse.pick_waves` — extended
`geography_level` (`Province | City | Suburb`), `geography_value`, `period_from`, `period_to`,
`company_scope`. Built from the order's **snapshotted** geography, not a live address join (ADR-113).

### `warehouse.pick_wave_line_breakdowns`
The per-order split of a grouped wave line: `pick_wave_line_id`, `order_id`, `order_line_id`,
`quantity`. Sums exactly to the grouped quantity — a check the consolidation step depends on.

### `warehouse.count_schedules`
`cadence`, `scope` (zone / class / value band / supplier), `slow_mover_days`, `random_sample_size`,
`next_run_at`. Generates Stage 13 `cycle_counts`; adds no second counting model (ADR-115).

---

## 4o. Tables in `fieldsales` (Stage 14b)

### `fieldsales.reps` / `fieldsales.rep_territories` / `fieldsales.rep_companies`
The user, the customers and/or geography they cover, the companies they may sell for, and their
visibility profile (cost? margin?).

### `fieldsales.pro_forma_orders` / `_lines`
Proposal documents. Own `PF-` sequence per company. Lines carry quantity, uom, **pack size**, quoted
price, the price-list and promotion snapshot, and the availability `as_at` shown to the rep. Status:
`Draft | Submitted | Approved | Amended | Rejected | Expired | Converted`. Posts nothing, reserves
nothing (ADR-107).

### `fieldsales.pro_forma_credit_notes` / `_lines`
The same, against a supplied invoice and its lines, with a reason code. Approves into a Stage 10 credit
note inside the original invoice's company.

### `fieldsales.rep_targets`
Versioned per rep, per company, per period. A target change never restates a measured period.

### `fieldsales.rep_performance_snapshots`
Closed-period, immutable, per rep per company with the group roll-up: captured, converted, invoiced,
credited, net, margin (where permitted), collections, customer coverage. A recomputation writes a new
version with a reason (ADR-110).

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
| `StockLocation` | Bidirectional | CloudWins | A store names its own back room; head office adds a distribution centre. Same shape as `Store`. |
| `StockLedgerEntry` | StoreToCloud | AppendOnly | Stock physically moves at the store, and the ledger accumulates. Two nodes writing independent movements merge without overwriting — the same reason `AuditEntry` is append-only (ADR-005). |
| `StockBalance` | NodeLocal | LastWriterWins | **Deliberately not replicated as a value** (ADR-069). A running total is not safely mergeable: two nodes each applying their own delta to a synced total would double an overlapping movement or diverge silently, and neither surfaces as a conflict for anybody to review. Each node rebuilds its own from the entries it has applied. |
| `StockTransfer` | StoreToCloud | AppendOnly | The document is written once, when both its ledger entries already exist, and never edited. |
| `StocktakeSession` | StoreToCloud | StoreWins | The count happens on one store's floor; the cloud observes the result rather than participating in it. |
| `StocktakeLine` | StoreToCloud | StoreWins | Follows its session. |
| `TillSession` | StoreToCloud | StoreWins | A shift happens at one drawer in one shop. The cloud observes the cash-up; it never participates in one, and two stores can never contend over the same till. |
| `Sale` | StoreToCloud | StoreWins | A sale is rung up in one place and is frozen the moment it completes. `StoreWins` rather than `AppendOnly` because a sale is legitimately mutable while it is open — lines and tenders arrive one at a time — and the store is the only node that can be editing it. |
| `SaleLine` | StoreToCloud | StoreWins | Follows its sale. |
| `SaleTender` | StoreToCloud | AppendOnly | Money that changed hands. `IImmutableRecord`, so it accumulates and never overwrites — the same reason `AuditEntry` and `StockLedgerEntry` are append-only. |
| `ReceiptPrint` | StoreToCloud | AppendOnly | A print log that a later write can overwrite is not a log. |
| `PriceList` | Bidirectional | CloudWins | Head office publishes a national list; a branch maintains its own. Same shape as `Store` and `Item`, and the cloud is where a multi-store estate settles a genuine collision. |
| `PriceListLine` | Bidirectional | CloudWins | Follows its list. |
| `Promotion` | Bidirectional | CloudWins | A special is set centrally for a chain and locally for a branch clearance; both are normal, and head office reconciles. |
| `PromotionLine` | Bidirectional | CloudWins | Follows its promotion. |
| `SalesReturn` | StoreToCloud | StoreWins | Goods come back over one counter, and the document is frozen the moment it completes. `StoreWins` rather than `AppendOnly` for the reason `Sale` is: it is legitimately mutable while it is a draft, and the store is the only node that can be editing it. |
| `SalesReturnLine` | StoreToCloud | StoreWins | Follows its return. |
| `PriceOverrideLog` | StoreToCloud | AppendOnly | A log a later write can overwrite is not a log. `IImmutableRecord`, so it accumulates — the same reason `AuditEntry`, `StockLedgerEntry` and `ReceiptPrint` are append-only. |
| `ImportBatch` | StoreToCloud | StoreWins | An import happens at the store, against that store's data, and the cloud observes the outcome — how many rows, what they did, who committed it. The store is the only node that can be editing one. |
| `ImportColumnMapping` | StoreToCloud | StoreWins | Follows its batch. |
| `ImportRow` | StoreToCloud | StoreWins | Follows its batch. `StoreWins` rather than `AppendOnly` because a row is legitimately rewritten several times before it settles — a verdict at validation, an outcome at commit, a compensation at rollback — and all of it happens on the one node that owns the batch. |
| `ImportMappingTemplate` | Bidirectional | CloudWins | The one entity here head office has a reason to own: a chain negotiating a supplier's file format once and pushing the mapping to every branch is the point of templates. A branch still saves its own for a local supplier, and the cloud settles a genuine collision — the same shape as `PriceList`. |
| `PurchaseRequisition` | Bidirectional | CloudWins | Demand is raised in a shop that has run out and by a head-office buyer working an estate; both are normal. Head office reconciles, the same shape as `Item`. |
| `PurchaseRequisitionLine` | Bidirectional | CloudWins | Follows its requisition. |
| `Rfq` | Bidirectional | CloudWins | Sourcing is the act most likely to happen centrally — a chain asks three suppliers once for every branch — while a store still sources locally for a local line. |
| `RfqLine` | Bidirectional | CloudWins | Follows its RFQ. |
| `RfqResponse` | Bidirectional | CloudWins | A quote is frozen on submission (business rule 2), so the policy rarely arbitrates anything. `CloudWins` rather than `AppendOnly` because a response *is* legitimately edited before it is submitted, and Stage 21b will have a connected supplier authoring one over the network. |
| `RfqResponseLine` | Bidirectional | CloudWins | Follows its response. |
| `PurchaseOrder` | Bidirectional | CloudWins | The commitment, and the one document in this chain head office most needs to own — buying centrally is the ordinary reason a multi-store estate exists. A branch still raises its own. Frozen once issued (§7 rule 7), so the policy arbitrates only over drafts. |
| `PurchaseOrderLine` | Bidirectional | CloudWins | Follows its order. Its `received_` and `invoiced_` running totals are advanced only on the node that owns the order, inside the transaction that writes the receipt or releases the match. |
| `GoodsReceipt` | StoreToCloud | StoreWins | Goods physically arrive at one door, and the cloud observes it. The same shape as `SalesReturn`: legitimately mutable while it is a draft, frozen once completed, and only the store can be editing it. |
| `GoodsReceiptLine` | StoreToCloud | StoreWins | Follows its receipt. |
| `SupplierInvoiceMatch` | Bidirectional | CloudWins | Matching an invoice is an accounts-payable act, and AP is usually head office's — but a single-store shop does it at the back-office machine. Frozen once released (§7 rule 7). |
| `SupplierInvoiceMatchLine` | Bidirectional | CloudWins | Follows its match. |
| `SupplierScorecard` | Bidirectional | CloudWins | A snapshot over a closed period, written once and never edited (ADR-084), so the policy arbitrates nothing in practice. Head office is where an estate compares suppliers across branches. |
| `Zone` | Bidirectional | CloudWins | A store names its own receiving dock; head office lays out a new distribution centre's zones. Same shape as `StockLocation`. |
| `Bin` | Bidirectional | CloudWins | Follows its zone. |
| `BinStock` | NodeLocal | LastWriterWins | Deliberately not replicated as a value, for the same reason `StockBalance` is not (ADR-069 applied one level down): a running total is not safely mergeable, and each node rebuilds its own from the movements it has applied. |
| `BinStockMovement` | StoreToCloud | AppendOnly | Bin-level stock physically moves at one store, and the ledger accumulates — the same reason `StockLedgerEntry` is append-only. |
| `PutawayTask` | StoreToCloud | StoreWins | Shelving happens at one store's floor and is legitimately mutable while pending (a picker confirms it in more than one call); the cloud observes the outcome. |
| `PickWave` | StoreToCloud | StoreWins | A wave is worked at one location and is legitimately mutable while open — lines are added, released, picked. The store is the only node that can be editing one. |
| `PickTask` | StoreToCloud | StoreWins | Follows its wave. |
| `PackTask` | StoreToCloud | StoreWins | One packing record per wave, written once the wave is picked; the store is the only node that can write it. |
| `ShipmentConfirmation` | StoreToCloud | AppendOnly | The point stock leaves a store's door. `IImmutableRecord` — a correction is a new shipment, not an edit, the same shape `StockTransfer` uses. |
| `CycleCount` | StoreToCloud | StoreWins | A physical count happens on one store's floor; the cloud observes the result. Same shape as `StocktakeSession`. |
| `CycleCountLine` | StoreToCloud | StoreWins | Follows its count. |
| `SalesOrder` | StoreToCloud | StoreWins | An order is taken at one counter or phone line and is legitimately mutable while it works through allocation and fulfilment — lines confirm, allocate, ship, backorder. The store taking it is the only node that can be editing it, the same shape `Sale` and `GoodsReceipt` both use. Unlike `BinStock`/`StockBalance`, this is the order itself, not a projection, and must sync. |
| `SalesOrderLine` | StoreToCloud | StoreWins | Follows its order. |
| `SalesOrderReturn` | StoreToCloud | StoreWins | Goods come back at one counter, and the document is frozen the moment it completes — the same shape `SalesReturn` and `GoodsReceipt` both use. |
| `SalesOrderReturnLine` | StoreToCloud | StoreWins | Follows its return. |

### Added by Stages 06c, 07c, 08c, 13b and 14b

| Entity | Direction | Conflict policy | Why |
|---|---|---|---|
| `Company` | CloudToStore | CloudWins | A legal entity and its connection details are head-office data. A store reads them; it does not invent a company. Registry-database entity. |
| `CompanyGroup` / `CompanyGroupMember` | CloudToStore | CloudWins | Group membership is a head-office decision. Registry. |
| `SagaIntent` / `SagaLeg` | StoreToCloud | AppendOnly | The record of a cross-company operation, written where it was initiated. Immutable; a leg's acknowledgement is a new state, and re-driving after a restore is safe because legs are idempotent (ADR-120). Registry. |
| `CreditGroup` / `CreditGroupMember` | CloudToStore | CloudWins | The limit is set centrally. Registry. |
| `CreditHold` / `CreditExposureEntry` | StoreToCloud | AppendOnly | Exposure accrues where trade happens; holds expire on their own. Append-only is what makes replay after an outage safe. Registry. |
| `CatalogRoutingIndexEntry` | CloudToStore | CloudWins | A projection of catalogue data, which is already CloudToStore. Registry. |
| `GroupReceipt` / `GroupReceiptAllocation` | StoreToCloud | AppendOnly | Money is captured where the customer paid. `IImmutableRecord` once allocated — a correction is a reversal. Registry; the AR receipts its legs create replicate from their own company databases as normal. |
| `GroupPayment` / `GroupPaymentAllocation` | StoreToCloud | AppendOnly | Same, outbound. |
| `InterCompanyClearingIntent` | StoreToCloud | AppendOnly | A posted pair, never edited; settled only when both companies acknowledge. Registry. |
| `StockReservation` | StoreToCloud | AppendOnly | A hold is taken where the stock is, in that company's own database, and released by a new entry. The append-only shape is what makes it safe to replay after a restore. |
| `CountSchedule` | CloudToStore | CloudWins | A counting policy is head office's; the counts it generates are the store's. |
| `PickWaveLineBreakdown` | StoreToCloud | StoreWins | Follows its wave. |
| `Rep` / `RepTerritory` / `RepCompany` | CloudToStore | CloudWins | Who a rep is and what they may sell is head-office data. |
| `ProFormaOrder` / `_Line` | Bidirectional | StoreWins | Captured on a rep's device offline, approved at the back office. The capturing node owns the content; approval is a separate, server-side transition. Replayed through the idempotent sync batch path. |
| `ProFormaCreditNote` / `_Line` | Bidirectional | StoreWins | Same. |
| `RepTarget` | CloudToStore | CloudWins | Targets are set centrally and versioned. |
| `RepPerformanceSnapshot` | StoreToCloud | AppendOnly | A closed period, immutable. A recomputation is a new version. |
| `Operator` | CloudToStore | CloudWins | Projected from the signed licence. A store never mints one. Registry. |
| `CompanyLink` | Bidirectional | CloudWins | Proposed and accepted at either end; the cloud is authoritative on status so a revocation propagates even if one store is offline. Registry. |
| `Premises` / `PremisesOccupancy` / `PremisesBinLayout` | CloudToStore | CloudWins | Site and layout are head-office data; the mirror into each company's `warehouse` schema is a saga leg, not replication. Registry. |
| `RegistryUser` / `RegistryUserCompanyAccess` | CloudToStore | CloudWins | The directory and its grants are central; per-company role catalogues stay where Stage 02 put them. Registry. |
| `RegistryTerminal` | Bidirectional | CloudWins | Registered at a store, entitled centrally. Registry. |
| `TradingSession` / `_Segment` / `_Line` / `_Tender` | StoreToCloud | AppendOnly | A basket happens at one till. Immutable once completed; a correction is a return. The session id is the replay idempotency key (ADR-125). Registry. |
| `ContactBinding` | Bidirectional | CloudWins | Created tenant-side, usable at any node; revocation must propagate promptly, so the cloud wins. Registry. |
| `Conversation` / `ConversationTurn` | StoreToCloud | AppendOnly | Append-only transcript, retained per the tenant's policy. Company database. |

Stage 04 turns this registry into the sync protocol and extends `docs/SYNC_AND_BACKUP.md`. Stage 06
adds the five rows above; see `docs/SYNC_AND_BACKUP.md` §3 for the same registry with schema and
one-line rationale per entity.

**Replication runs per database.** Each company database has its own outbox, inbox and cursors, and the
cloud tier mirrors the store's layout — a registry and N company databases (ADR-120). A restore of one
company database is a supported operation on its own; afterwards the registry re-drives that company's
outstanding intent legs, which is safe because every leg is idempotent.

**The uploaded file itself is deliberately absent from this registry.** `IImportFileStore` keeps the
bytes outside the database entirely, and they do not replicate: the store server is where an import
happens and where the file was uploaded, and the cloud gets the batch, the rows and the outcome, which
is what a head office asks about. R10 also applies — an uploaded customer list is exactly the sort of
document that has no business leaving the premises for vendor purposes.

---

## 6. Migrations

`src/VumaRetail.Infrastructure/Migrations`, history table `platform.__ef_migrations_history`. The
registry database has its own chain and its own history table, and is always migrated first.

**Migrations fan out** (ADR-117). The runner iterates every company database in the registry, applying
the same chain with bounded parallelism, recording `schema_version` and `migration_state` per company. A
company whose version is behind the running binary is **not served** — its endpoints return an
actionable failure naming the company and the pending migration, rather than executing against a schema
the code does not expect. A partially migrated tenant is a first-class, reportable state, and migration
is resumable and re-runnable. A company restored from backup arrives at whatever version it was taken at
and is migrated forward by the same path before it is served.

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
