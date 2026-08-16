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
| `backup` | the snapshot ledger | 04 |
| `licensing` | licences, leases, activations, entitlements, metering | 04b |
| `catalog` | items, variants, barcodes, units of measure | 06 |
| `partners` | suppliers, customers, and partners who are both | 06 |
| `finance` | chart of accounts, GL, AR, AP, banking, tax, posting rules | 07 |
| `inventory` | stock locations, the stock ledger, balances, transfers, stocktakes | 08 |

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
