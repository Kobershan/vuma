# DECISIONS (ADR log) — Vuma Retail

Every architectural choice lives here. **Decisions marked LOCKED are settled — do not re-open them,
do not "improve" them mid-stage.** New decisions get appended with the next number.

Format: `ADR-NNN — Title` / Status / Context / Decision / Consequences.

---

## ADR-001 — .NET 9 + WPF for the Windows client — **LOCKED**
**Context.** Target is Windows-installed retail software driving physical hardware (receipt printers,
cash drawers, scanners, scales, card terminals) with a touch-first till UI.
**Decision.** C# 13 / .NET 9. WPF for the desktop shell using MVVM (`CommunityToolkit.Mvvm`) hosted in
a generic host so DI, config and logging match the server code.
**Consequences.** Best-in-class Windows hardware/driver access and printing. Not cross-platform — an
acceptable trade because the requirement is explicitly Windows. Android is a separate native client
talking to the same API, not a shared-UI framework.

## ADR-002 — PostgreSQL 16 for store and cloud — **LOCKED**
**Decision.** Same engine both tiers: identical SQL, identical migrations, trivially restorable
between them. Schema-per-module (`sales`, `inventory`, `hr`, …) inside one database.
**Consequences.** No licence cost per store. Logical replication and PITR available for the backup
story. Requires PostgreSQL to be installed by the store installer (handled in Stage 31).

## ADR-003 — SQLite offline cache on every terminal — **LOCKED**
**Decision.** Each Windows terminal keeps a local SQLite file holding the catalogue, prices,
promotions, customers, and an outbound operation queue.
**Consequences.** Requirement R1 (POS never stops) is satisfied even if the LAN or store server dies
mid-trade. Cost: a three-tier sync (terminal → store → cloud) which Stage 04 must handle generically.

## ADR-004 — UUID v7 primary keys — **LOCKED**
**Decision.** All PKs are UUID v7 (time-ordered), generated client-side.
**Consequences.** Offline terminals can create records without a round-trip and without ID collisions.
Time-ordering keeps B-tree index locality (unlike UUID v4). Slightly larger keys than bigint —
accepted.

## ADR-005 — Append-only stock ledger, not a quantity column — **LOCKED**
**Decision.** `inventory.stock_ledger` is append-only. On-hand quantity is a materialised projection
(`inventory.stock_balance`) rebuilt from the ledger and maintained transactionally.
**Consequences.** Stock is always explainable ("why is this 3 and not 5?"), sync conflicts on stock
become non-destructive merges of independent entries, and stocktakes/valuation become straightforward.
Cost: more rows; mitigated with monthly ledger snapshots.

## ADR-006 — Transactional Outbox + idempotent Inbox for all sync — **LOCKED**
**Decision.** Any state change that must reach another tier writes an outbox row in the same DB
transaction. A background dispatcher ships batches; the receiver deduplicates on `(source_node,
operation_id)`.
**Consequences.** Exactly-once *effect* over an at-least-once transport, resumable after any
crash/outage of any length.

## ADR-007 — Hybrid Logical Clocks + per-entity conflict policy — **LOCKED**
**Decision.** Every replicated row carries an HLC stamp. Conflict resolution policy is declared per
entity type: `LastWriterWins`, `StoreWins`, `CloudWins`, `AppendOnly`, or `ManualReview`.
**Consequences.** Deterministic merges without a global clock. Ambiguous business conflicts (e.g. two
stores editing one supplier) route to a review queue instead of silently losing data.

## ADR-008 — API-first; the desktop app is just a client — **LOCKED**
**Decision.** Every capability is exposed by the versioned REST API before any UI is built for it.
The WPF app and the Android app both consume it. No private back-channels.
**Consequences.** The Android admin app and any future integration get everything for free. Slightly
more work per feature; that cost is the point.

## ADR-009 — Hand-rolled CQRS dispatcher, not MediatR — **LOCKED**
**Decision.** A minimal `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>`
abstraction with Scrutor assembly scanning and an explicit pipeline (validation → transaction →
audit → outbox → logging).
**Consequences.** No third-party licensing exposure, ~200 lines we control, and a pipeline we can
inspect. Handlers stay portable if we ever change our mind.

## ADR-010 — Modular monolith, not microservices — **LOCKED**
**Decision.** One deployable store server with strict module boundaries (schema-per-module, no
cross-module table joins, module-to-module calls go through published contracts + domain events).
**Consequences.** A single store server is easy to install, back up and support in a retail
environment. Boundaries are enforced by an architecture test (NetArchTest) so extraction later is
possible if ever needed.

## ADR-011 — Velopack for auto-update, WiX for first install — **LOCKED**
**Decision.** WiX v4 bootstrapper provisions prerequisites (PostgreSQL, .NET runtime, service
registration, firewall rules) on first install; Velopack handles all subsequent delta updates for
both the desktop app and the store server.
**Consequences.** Stores self-update over the internet without a technician visit; a rollback channel
is available if an update misbehaves.

## ADR-012 — Immutable posted documents — **LOCKED**
**Decision.** Invoices, credit notes, GRNs, stock movements and payroll runs cannot be edited after
posting. Corrections are new reversing documents.
**Consequences.** Audit and tax integrity; sync conflicts on financial records become impossible by
construction.

## ADR-013 — Permission model is claim-based and module-scoped — **LOCKED**
**Decision.** Permissions are strings shaped `module.entity.action` (e.g. `inventory.stocktake.approve`)
declared by each module in a catalogue at startup. Roles are bags of permissions; users hold roles
scoped to store(s).
**Consequences.** New modules add permissions without touching identity code; the Android app can ask
`/api/v1/me/permissions` and drive its own navigation.

## ADR-014 — Tax as a rules engine — **LOCKED**
**Decision.** Tax groups × tax jurisdictions × effective-dated rates, evaluated per line. VAT 15%
inclusive is *seed data*, not code.
**Consequences.** Rate changes are a data edit; multi-jurisdiction and zero-rated/exempt items work
out of the box.

## ADR-015 — Import is a two-phase staged pipeline — **LOCKED**
**Decision.** File → parse → staging tables → validate → **preview/diff** → commit, with a
reversible batch ID. Column mapping is saved per source as a reusable profile.
**Consequences.** Bad supplier spreadsheets can never corrupt live data; a botched price-list import
is one click to roll back.

---

# Revision — added modules

## ADR-016 — Full double-entry financial core, built at Stage 07 — **LOCKED**
**Context.** The original plan had customer accounts and supplier invoices but no general ledger. That
is a POS with stock control, not an ERP: no trial balance, no P&L, no balance sheet, no VAT return, no
way to prove the books are right.
**Decision.** A real double-entry GL with a chart of accounts, analysis dimensions (store, department,
cost centre, project, channel, employee), accounting periods, and a **posting rules engine**. Modules
raise financial domain events; the engine turns them into balanced journals. No module ever names a GL
account directly — enforced by an architecture test.
**Consequences.** Every later module must define its posting rules as part of its stage. Sub-ledgers
(AR, AP, stock, cash, WIP, loyalty liability) must reconcile to their control accounts continuously,
which is asserted by an automated daily job and blocks period close on variance. Finance sits at Stage
07, before Inventory and POS, because those modules post to it.

## ADR-017 — Loyalty is its own module with a public API — **LOCKED**
**Context.** Loyalty was buried as a sub-feature of Sales. But loyalty must work at the till, on the
website, in a consumer app and at coalition partners, with one balance, and it creates a real balance
sheet liability.
**Decision.** A separate `loyalty` module with an append-only transaction ledger (balance is a
projection, exactly like stock), a configurable earn/burn/tier rule engine, and a public API served
from a separate host for storefronts, apps and partners.
**Consequences.** Offline redemption becomes an explicit, reconcilable risk with a tenant-chosen policy
rather than an accident. Points liability posts to the GL and reconciles. Partners get scoped access to
one member without any other customer data.

## ADR-018 — Headless commerce: Vuma is the backend of record — **LOCKED**
**Context.** "Connect an ecommerce website" can mean Vuma owns commerce, or Vuma syncs with an
existing storefront platform. Both are legitimate and tenants will want both.
**Decision.** Do both through one design. A public Storefront API (catalogue, cart, checkout, account)
makes Vuma the commerce backend for any frontend; an `IChannelConnector` abstraction covers existing
storefronts, marketplaces and EDI partners. In both cases **web orders enter the same Stage 14 order
pipeline** and **web prices come from the same Stage 10 pricing engine as the till**.
**Consequences.** No divergent order model, no second price list that drifts. A price-parity test
asserts the till and the website produce identical baskets. The reference storefront in
`samples/storefront/` exists to prove the API is actually usable, not to be a product.

## ADR-019 — One workflow, approval, notification and document engine — **LOCKED**
**Context.** Procurement, HR, imports, quality, projects and finance all need approvals, escalations,
notifications and file attachments. Ten modules each building their own is ten inconsistent
implementations and ten places for a separation-of-duties rule to be forgotten.
**Decision.** Build them once at Stage 05, before any module needs them. Approval policies are data.
Separation of duties is enforced centrally in the engine. One unified approval inbox aggregates every
pending item across every module — which is exactly what the Android app and dashboard consume.
**Consequences.** Later stages configure rather than build. An architecture test flags any handler
gating on a value threshold without going through `IApprovalService`.

## ADR-020 — Planning is advisory, never automatic — **LOCKED**
**Context.** MRP/DRP output that silently creates real purchase orders is how businesses end up with a
warehouse full of the wrong thing.
**Decision.** Planning runs write to their own tables and produce *suggestions*. A human firms a
suggestion into a real PO, transfer or work order. Auto-release is possible but must be explicitly
enabled per item class with a value cap. Every planned order must be explainable by pegging back to
its source demand in one click.
**Consequences.** Planners trust it, because they can see why. Forecast accuracy is measured
continuously (MAPE, bias) so a degrading model is visible rather than quietly wrong.

## ADR-021 — A separate public API host — **LOCKED**
**Context.** The storefront and loyalty APIs face the open internet and untrusted partners. The
internal API assumes an authenticated staff user and returns cost, margin and supplier data.
**Decision.** `VumaRetail.PublicApi` is a separate deployable host with its own auth model
(publishable/secret/partner keys plus member and customer tokens), its own rate limits, its own caching
posture, and — critically — **its own DTOs**. Internal fields are not properties on any type this host
can serialise.
**Consequences.** A leak requires a deliberate new field on a public DTO rather than a forgotten filter.
Stages 20 and 21 each carry a security suite that attacks every filter, sort and expand parameter to
prove nothing internal is reachable.

## ADR-022 — Statutory boundaries are provider interfaces, not core code — **LOCKED**
**Context.** E-invoicing mandates, fiscal printer regimes, bank file formats, payroll statutory
calculations and payment gateways all vary by country and change on political timetables.
**Decision.** Each is an interface — `IEInvoiceProvider`, `IFiscalDevice`, `IPaymentGateway`,
`IAccountingConnector`, `IBankFileFormat`, `IEdiTransport`, `IChannelConnector` — with a fake
implementation shipped so the system is fully testable, and real implementations added as
configuration when a market requires them. Vuma prepares payroll data; it is not a statutory payroll
engine, and that boundary is stated in the product documentation.
**Consequences.** Entering a new country is a provider implementation, not a redesign. Anything needing
real credentials is logged in `PROGRESS.md` under "Deferred" rather than blocking a stage.

---

# Revision 2 — SaaS licensing and control plane

## ADR-023 — Licensing enforces on capability, never on trade or data — **SUPERSEDED by ADR-027**
**Context.** Vuma is sold as a monthly SaaS subscription but runs on the customer's own Windows
hardware, frequently offline. Aggressive licence enforcement in that setting means a shop's till
refuses to sell because their fibre is cut on a Saturday.
**Decision.** Enforcement escalates through *capability* — notices, then restricted back-office writes,
then read-only — and **never** blocks the sale flow, exports, reporting, backup or the DR restore path.
A monthly signed licence is refreshed by a 72-hour lease; the offline grace ladder runs 45 days, and
any successful heartbeat resets it instantly. Subscription-suspended (we know they can reach us) is a
separate, faster ladder from offline (we cannot tell).
**Consequences.** Vuma can never brick a customer's business, at the cost of a determined non-payer
trading for weeks.
**Superseded.** The vendor's commercial position is that Vuma is a subscription product and an unpaid
subscription means no product. See ADR-027, which replaces this decision. The engineering safeguards
that prevented *accidental* lockout survive into ADR-027 and are, if anything, more important there.

## ADR-024 — The control plane is a separate deployment with minimised telemetry — **LOCKED**
**Context.** The vendor needs to see who is using Vuma and how much. That service can also mint
licences and disable stores, and its database describes every customer's operations — it is the single
highest-value target in the system. Meanwhile the vendor's customers must be able to answer their own
regulators about what leaves their premises.
**Decision.** `VumaRetail.ControlPlane` is a separate deployable with its own database, credentials
and audit log, with the licence signing key in a KMS/HSM. Telemetry is **counts and health only** —
never customer names, sales line detail, employee data or any document content — enforced by a
whitelist in code and asserted by test. Vendor staff have **no path to tenant business data** without a
tenant-granted, time-boxed, dual-audited support grant that shows a banner in the tenant's own UI.
**Consequences.** A control plane compromise exposes usage metadata, not customers' trading data. The
vendor can honestly tell prospects what is collected. Support is slightly slower because access must be
requested — accepted, and it is the correct default.

## ADR-025 — Vendor billing is separate from tenant finance — **LOCKED**
**Context.** Stage 07 gives each tenant a general ledger, and Stage 28 gives them subscription billing
for their own customers. It is tempting to reuse those for the vendor's own subscription revenue.
**Decision.** The control plane keeps its own subscription, invoice and revenue records. No vendor
revenue appears in any tenant's ledger, and no tenant's ledger is readable by the control plane. The
vendor's own accounting is fed by a normal export (and may itself be a Vuma tenant, connected through
the ordinary integration — never a back door).
**Consequences.** Clean separation of books, no risk of a tenant seeing vendor pricing data or vice
versa, and the vendor's finance function is not coupled to a customer's data model.

## ADR-026 — Detection over prevention for piracy — **LOCKED**
**Context.** Software running on hardware the customer controls can always eventually be cracked.
Escalating client-side obfuscation costs engineering time indefinitely and buys weeks.
**Decision.** Ship reasonable client-side hardening (signed binaries, pinned Ed25519 licence key,
integrity self-check, tamper flags) and invest the real effort in **server-side detection** —
duplicate fingerprints on one licence, counter rollback, clock tamper, impossible travel, terminal
overage, and colliding document number series across installs. Detections go to a human-reviewed abuse
queue and **never auto-disable anything**. The genuine moat is service dependency: cloud backup,
verified restore, updates, multi-store sync, the Android app and the public APIs.
**Consequences.** A false positive cannot kill a paying customer's store. Piracy becomes visible and
addressable commercially rather than being fought in an unwinnable client-side arms race.


## ADR-027 — No current licence, no software — **SUPERSEDED by ADR-028** *(superseded ADR-023)*
**Context.** Vuma is sold as a monthly SaaS subscription running on the customer's own Windows
hardware. ADR-023 enforced only on capability, leaving a non-paying customer trading indefinitely. The
vendor's commercial position is that an unpaid subscription means no product, and that position is the
vendor's to set.
**Decision.** When a subscription is not current, the application does not open: no POS, no back
office, no API, no reporting. Two paths reach that state and they are deliberately different — **Path
A (cannot verify)** runs on the last valid lease for a configurable tolerance window (default 14 days)
before locking, and **Path B (non-payment)** locks after a *completed* dunning cycle with delivered
notifications plus a configurable grace (default 0). Three vendor-issued, offline-capable unlock
mechanisms exist: emergency access codes, export-only unlock, and grace extension.
**Consequences.** The commercial lever is real and immediate. In exchange, three engineering
guarantees become mandatory, because without them this policy destroys the business it is meant to
protect:
1. A control plane outage on the vendor's side must **never** lock a customer out — otherwise one bad
   deployment locks out the entire customer base at once.
2. A lockout must require a known subscription state. A failed charge, a network fault, a clock change
   or a hardware swap must never cause one.
3. Emergency access codes must work fully offline, or the vendor personally handles every
   Friday-evening payment and every bank glitch.
The statutory-records exposure (customers must retain tax records for years) is handled by the
vendor-granted export unlock, not by weakening the lockout, and the same position goes into the licence
terms so it is contractual rather than improvised during a dispute.


## ADR-028 — Lapsed subscription means read-only, not lockout — **LOCKED** *(supersedes ADR-027)*
**Context.** Vuma is billed on a recurring subscription mandate. ADR-027 closed the application
entirely on lapse. The vendor's settled position is that a lapsed tenant keeps sight of their data and
loses the ability to act on it.
**Decision.** The enforcement ladder ends at **read-only**: full read, report, reprint and export
access; no writes of any kind, including sales. Selling is a write, so the till stops — that is the
commercial lever. Hard lockout survives only as a manual, two-person, audited vendor action for
confirmed abuse, never as an automatic ladder step. Four rules are non-negotiable: read-only must be
deliberate (completed dunning, known state), the payment and card-update screens stay writable so a
customer can always pay from inside the product, already-captured data still syncs and backs up, and a
vendor-side outage never restricts anyone.
**Consequences.** The commercial lever stays sharp — a business that cannot ring up a sale will act
within the hour — while the statutory-records exposure and the "locked out by a silently expired card"
disaster both disappear. Recovery is automatic within 60 seconds of payment. The remaining cost is that
a non-paying tenant retains visibility of their own historical data indefinitely, which is accepted and
is the correct trade.

## ADR-029 — Involuntary payment failure is a product problem, not a billing problem — **LOCKED**
**Context.** Most failed subscription charges are involuntary: expired cards, cards reissued after
fraud, limits, mandate lapses, reversed debit orders. The customer intends to pay and does not know
anything went wrong. Left alone, this is silent revenue loss and support load.
**Decision.** Prevent the failure rather than react to it: notify 30 and 7 days before a stored card
expires, support mandate re-authentication reminders, use gateway account-updater services where
available, retry on a schedule tuned to paydays rather than blindly, keep multiple payment methods with
a fallback, and put a working payment-method update page in every dunning email **and inside the
product while read-only**. The dunning ladder pauses rather than advances if its notifications are not
recorded as delivered.
**Consequences.** Most involuntary failures are resolved before a customer ever sees a banner, which
protects both revenue and the relationship. Stage 30b owns the implementation.

---

# Revision 3 — Stage 00 foundation

## ADR-030 — Central package management — **LOCKED**
**Context.** The solution will reach roughly fifteen projects. Package versions declared per project
drift, and the drift surfaces as a diamond-dependency failure at runtime in a store, not at build time
on a developer's machine.
**Decision.** `Directory.Packages.props` at the repository root holds every version.
`PackageReference` elements in project files carry no `Version` attribute. Transitive pinning is on.
**Consequences.** One place to audit for the vulnerability scan and one place to upgrade. A project
that wants a different version of a package cannot quietly have one, which is the point.

## ADR-031 — Windows-only projects are created by the stage that needs them — **LOCKED**
**Context.** `CLAUDE.md` §5 lists `VumaRetail.Desktop`, `.Hardware`, `.Imports`, `.Reporting`,
`.ControlPlane` and `VumaRetail.UiTests`. The desktop, hardware and UI-test projects target
`net9.0-windows` (WPF, FlaUI) and **cannot build on Linux at all**. Creating them in Stage 00 would
mean either a solution that no non-Windows machine can build, or a set of empty shells excluded from
the build and therefore checked by nothing.
**Decision.** Stage 00 creates only the cross-platform skeleton: `Domain`, `Application`, `Contracts`,
`Infrastructure`, `Sync`, `StoreServer`, `CloudApi`, `PublicApi`, and the `UnitTests` and
`ArchitectureTests` projects. The rest are created by the stages that first need them — `Imports` at
11, `Desktop` and `Hardware` at 09, `Reporting` at 09, `ControlPlane` at 30b, `UiTests` alongside the
desktop shell.
**Consequences.** `dotnet build` and `dotnet test` stay green on any platform for as long as possible,
so CI is cheap and the Definition of Done is genuinely checkable. The cost is that the repository
layout in `CLAUDE.md` §5 describes the finished shape rather than the current one — stated here so it
does not read as an omission. From Stage 09 onward a Windows machine or VM is mandatory, not optional.

## ADR-032 — FluentAssertions pinned to the 6.x Apache-2.0 line — **LOCKED**
**Context.** FluentAssertions moved to a paid commercial licence at version 8. Vuma is proprietary
commercial software, so the free-for-open-source terms do not apply to it. `docs/TESTING.md` names
FluentAssertions as the assertion library across every test project.
**Decision.** Pin to `6.12.2`, the last Apache-2.0 release, in `Directory.Packages.props` with the
reason written at the pin. Do not upgrade to 7.x or 8.x without either buying licences or migrating
the assertion style.
**Consequences.** No licence exposure and no per-seat cost. In exchange the test suite forgoes later
FluentAssertions features, which is a small loss. If a migration is ever wanted, the alternatives are
`Shouldly` (BSD) or plain xUnit assertions; that is a mechanical change and should be its own ADR
rather than an incidental package bump.

## ADR-033 — Money rounds midpoints away from zero — **LOCKED**
**Context.** `Money` stores at `decimal(18,4)` and rounds to a currency's presentation scale when a
line is finalised. .NET's default is banker's rounding (`MidpointRounding.ToEven`), which sends 2.005
to 2.00.
**Decision.** Midpoints round **away from zero**, so 2.005 becomes 2.01. Rounding happens once, when a
document line is finalised, never in the middle of a calculation — which is why the stored scale is 4
rather than 2.
**Consequences.** Matches what a customer gets when they check a receipt with a calculator, and what
South African retail practice expects. Banker's rounding is the better choice for large statistical
aggregates and the worse one at a till; the till wins. The total-vs-line reconciliation required by
`docs/TESTING.md` §3 is asserted in `MoneyTests`.

## ADR-034 — Command side-effect classification is mandatory and enforced at build time — **LOCKED**
**Context.** ADR-028 ends the enforcement ladder at read-only: no writes of any kind, including sales.
Stage 04b implements that as a single interceptor in the command pipeline rather than as checks
scattered across forty modules — but the interceptor can only refuse a write if it knows which
commands write. A module author who forgets to say would ship either a write that stays permitted
during a lapse (a hole in the commercial model) or a query that gets blocked (a broken promise that
reporting keeps working).
**Decision.** Every `ICommand<T>` carries `[CommandSideEffect(SideEffect.Write|ReadOnly)]`.
`SideEffect.Unclassified` is the zero value, so a missing or default attribute is detectable rather
than silently defaulting to either behaviour, and an architecture test fails the build on it. The
three ADR-028 carve-outs that stay writable in read-only — payment and card update, offline flush,
backup — are declared through `ReadOnlyExemption` on the same attribute, so the exemption set is a
single reviewable list, and a second architecture test fails if it grows past three.
**Consequences.** Built in Stage 00, before a single command exists, because retrofitting it across a
finished system is expensive and easy to get incompletely right — and an incomplete retrofit is
exactly the silent hole this prevents. Cost is one attribute per command.

---

# Revision 4 — Stage 01 persistence core

## ADR-035 — The concurrency token is an application-generated `bytea`, not `xmin` — **LOCKED**
**Context.** `CLAUDE.md` §7 rule 3 requires `row_version bytea` on every table. The Stage 00 handoff
in `PROGRESS.md` §5 proposed mapping it to PostgreSQL's `xmin` system column instead, which is the
idiomatic Npgsql approach and costs nothing to maintain.
**Decision.** Keep the rule: an 8-byte token generated by the application and written by
`AuditInterceptor` on every insert and update, marked `IsConcurrencyToken()`.
**Consequences.** The deciding reason is ADR-003. Every terminal caches the same rows in SQLite, and
SQLite has no `xmin` — an `xmin`-based token would mean the store server and the terminal compare
different things, at exactly the moment sync needs them to agree. It also means the token survives a
`pg_dump`/restore and a cross-tier copy, which matters for R4's restore path, whereas `xmin` is
regenerated by the receiving database and would silently invalidate every in-flight comparison. The
cost is one `Guid.NewGuid()` per write and eight bytes per row, which is not a cost.

## ADR-036 — Integration tests bind to a connection string, with Testcontainers as one supplier — **LOCKED**
**Context.** `docs/TESTING.md` §2 and `CONVENTIONS.md` §7 name Testcontainers as the way handler
tests reach real PostgreSQL. Docker is not installed on the machine Vuma is currently being built
on (`PROGRESS.md` §4.3), and `CLAUDE.md` §1 forbids marking a stage DONE whose tests cannot run.
Skipping the database tests when no Docker is present would report green while proving nothing.
**Decision.** `PostgresFixture` resolves a server in this order: `VUMA_TEST_POSTGRES` if it is set
and reachable, then Testcontainers, then a hard failure carrying the command that fixes it. It never
skips. `scripts/pg-test.sh` starts a throwaway cluster from the locally installed PostgreSQL server
binaries — no Docker, no sudo, no shared state — and prints the export line.
**Consequences.** Testcontainers remains the default on any machine with Docker, so `TESTING.md` §2
is honoured rather than overturned. CI uses a service container, which is the same engine with one
less daemon in the loop. The suite runs everywhere, and "no database available" is a loud failure
rather than a quiet skip. Cost: one small shell script, and two supported ways to get a server
instead of one.

## ADR-037 — Tenant and Store are platform entities, built in Stage 01 — **LOCKED**
**Context.** Stage 01 is the persistence core and builds no business module. But `tenant_id` and
`store_id` are on every table (§7 rule 3) and the global query filters are meaningless without
something to point at, so the mapping could not be proven against a real database without them.
**Decision.** `platform.tenants` and `platform.stores` are built here, as platform infrastructure
rather than as a module. They carry only what the platform needs — identity, localisation, activation
— and no commercial behaviour. Master data (items, partners, addresses) stays in Stage 06 and finance
in Stage 07.
**Consequences.** R8 (multi-store from the data model up) is satisfied from the first migration,
which is the only affordable time to do it — retrofitting a store dimension into a year of trading
data is a migration nobody wants to write. The risk is scope creep into Stage 06; the boundary is
that `Store` gets a structured address when Stage 06 builds structured addresses, not before.

---

# Revision 5 — Stage 02 identity

## ADR-038 — ASP.NET Core Identity contributes its hasher, not its stores — **LOCKED**
**Context.** `CLAUDE.md` §4 locks the auth stack to "ASP.NET Core Identity + JWT". The obvious
reading is `IdentityDbContext`, `IdentityUser`, `UserManager` and the EF stores. Three things make
that reading unworkable here, and none of them is a matter of taste:
1. `IdentityUser` cannot carry the `CLAUDE.md` §7 rule 3 columns. It has no `tenant_id`, no
   `store_id`, no `row_version`, no `sync_state`, no soft delete — so it cannot derive from `Entity`,
   cannot be mapped by `EntityConfiguration<T>`, and would sit outside both global query filters. A
   users table with no tenant filter is the exact failure R8 exists to prevent.
2. It cannot carry `[Replicated]`, so Stage 04's registry would have a hole in it on the one table
   that decides who may do anything.
3. `UserManager` owns its own persistence lifecycle and calls `SaveChanges` per operation. That
   conflicts with §7 rule 2 (every write goes through a command handler) and with the Stage 03
   pipeline's unit of work — a user created inside a larger transaction would commit on its own.
**Decision.** Take ASP.NET Core Identity's **`PasswordHasher<T>`** — PBKDF2-HMAC-SHA512, 100 000
iterations, versioned output — and nothing else. `User`, `Role`, `RolePermission`,
`UserRoleAssignment`, `Terminal` and `RefreshToken` are Vuma domain entities derived from `Entity`,
driven by command handlers like every other write in the system. Lockout counting, the security
stamp and normalisation are domain behaviour with tests, rather than framework behaviour without any.
**Consequences.** Identity obeys every persistence rule the rest of the system obeys: tenant
filtered, soft deletable, audited, replication-declared, committed by the pipeline. The cost is
roughly 200 lines of lockout and stamp logic that `UserManager` would have supplied — which is also
200 lines that are now testable on a virtual clock, which the lockout ladder needs. If SSO or MFA is
ever added, Identity's token providers can be adopted then on the same terms: take the primitive,
not the persistence model.

## ADR-039 — ASP.NET Core host wiring lives in `VumaRetail.Web` — **LOCKED**
**Context.** Stage 02 produces code that only makes sense inside an ASP.NET Core host — the
`HttpContext`-backed `IPrincipalAccessor`, tenant resolution middleware, the permission authorisation
handler and policy provider, the terminal certificate scheme, and the auth endpoints. The store
server, the cloud API and eventually the Android-facing API all need the same wiring. Putting it in
`Infrastructure` would mean a `FrameworkReference` to `Microsoft.AspNetCore.App` there — and
`Infrastructure` is referenced by the WPF desktop app from Stage 09, so every till would carry the
ASP.NET Core shared framework for code it never runs.
**Decision.** A new project, `src/VumaRetail.Web`, sitting above `Infrastructure` and below the
hosts. It holds the ASP.NET-specific wiring and nothing else. `VumaRetail.PublicApi` deliberately
does **not** reference it: that host has its own auth model and its own DTOs (ADR-021, §7 rule 14),
and `Web` carries internal contracts. An architecture test asserts both directions.
**Consequences.** `CLAUDE.md` §5's repository layout gains a project it does not list, in the same
spirit as ADR-031 — the layout describes the finished shape and this is how the shape is reached.
Stage 03 inherits a home for versioning, `ProblemDetails` and OpenAPI instead of having to invent one
or scatter them across three hosts.

## ADR-040 — Authentication is an edge service, not a command — **LOCKED**
**Context.** Every write in Vuma is an `ICommand` carrying `[CommandSideEffect]`, so Stage 04b's
read-only interceptor can refuse it during a subscription lapse (ADR-028, ADR-034). Signing in writes:
a refresh token row, a failure counter, a last-seen stamp. Classified as a `Write` it would be
refused while a tenant is read-only — and ADR-028 promises a lapsed tenant keeps full read, report,
reprint and export access, all of which is unreachable by somebody who cannot log in. Classified as
`ReadOnly` it would be a lie the architecture test cannot catch. Exempting it would spend one of the
three carve-outs the test caps at three, on something that is not a business write.
**Decision.** Sign-in, PIN sign-in, refresh rotation, sign-out and terminal authentication are an
`AuthenticationService` in the Application layer, invoked directly by the endpoint and committing
through `IUnitOfWork`. They never enter the command pipeline. Administrative identity changes —
creating a user, granting a role, setting a PIN, enrolling a terminal — are ordinary `Write` commands
and are refused while read-only, which is correct: a lapsed tenant should not be provisioning staff.
**Consequences.** The read-only guarantee holds without widening the exemption set, and the boundary
is stated rather than discovered: the pipeline governs what an authenticated caller may do, not
whether they may authenticate. Stage 04b's licence-safety suite should assert sign-in still works
under read-only, and that is now a property of the design rather than of a carve-out somebody
remembered to add.

## ADR-041 — POS PINs are unique tenant-wide and resolved per store — **LOCKED**
**Context.** A till takes a PIN and nothing else; making a cashier type an operator code first is the
difference between a two-second and a six-second sign-on, thirty times an hour. That only works if a
PIN identifies exactly one operator. Per-store uniqueness is the narrower constraint and was the
first design, but staff move between branches and cover shifts — a PIN that was unique when it was
set becomes ambiguous the week somebody is rostered elsewhere, and the collision surfaces as a sale
attributed to the wrong person.
**Decision.** A PIN is unique among the tenant's **live** operators, checked when it is set by
verifying the candidate against every stored PIN hash. Sign-in candidates are then narrowed to the
operators holding a role in the terminal's store, so a cashier at one branch cannot sign in at
another's till even with a valid PIN. A PIN is only ever accepted on an already terminal-authenticated
session: the certificate proves which till, the PIN says who is standing at it.
**Consequences.** Sign-on stays one gesture. The uniqueness check is O(staff) salted verifications,
which runs when a manager sets a PIN rather than on any sale path. The ceiling is 10 000 concurrent
4-digit operators per tenant before PINs must lengthen — well beyond any realistic estate, and the
PIN is 4–8 digits so lengthening is configuration. A soft-deleted or deactivated operator frees their
PIN, which is what "live" is doing in the rule.

## ADR-042 — The product is Vuma Retail; code is `VumaRetail.*`, repo and folder are `vuma` — **LOCKED**
**Context.** The product was renamed from Zenith Retail to **Vuma Retail** on 2026-08-10. "Zenith"
was woven through three layers that do not have to agree: the .NET identity (assemblies, root
namespaces, `VumaRetailDbContext`), the runtime identity (configuration section, connection-string
name, JWT issuer/audience, claim-type URIs, environment variables) and the hosting identity (the git
repository, the working folder). Leaving any of them behind would have meant a codebase that says one
name and a config file that answers to another.
**Decision.** All three move together, but not to the same string. The **code** keeps the two-word
form — `VumaRetail.Domain`, `VumaRetail.Web`, `Vuma__Jwt__Issuer`, `vuma:tenant`, `VUMA_TEST_POSTGRES`
— because the assembly names are the product name and shortening them would leave the namespace
saying less than the product does. The **repository and the working folder** are the bare `vuma`, as
the owner asked; they are addresses, not the product name, and short addresses are easier to type.
**Consequences.** The rename was mechanical and total: 145 files changed content, 25 directories and
the solution file were renamed, and no occurrence of the old name survives anywhere in the tree. It
was safe to do wholesale precisely because "zenith" never appeared as an English word in this
codebase — every hit was the product. Two things changed behaviour rather than just spelling and are
**breaking for any existing deployment**: the configuration section is now `Vuma__*` (a store server
started against an old `Zenith__*` environment gets defaults, not an error), and JWT claim types moved
from `zenith:*` to `vuma:*`, so tokens minted before the rename no longer resolve a tenant. Nothing is
deployed yet, so both cost nothing today; after Stage 09 either would need a migration note. The
PostgreSQL schema names were untouched — they are module names (`platform`, `identity`, `sync`,
`licensing`), never the product — so no database migration was needed. GitHub redirects the old
repository name, so a stale clone still fetches, but its remote should be re-pointed.

---

# Revision 6 — Stage 03 API platform

## ADR-043 — API versioning is URL-segment and hand-rolled — **LOCKED**
**Context.** `CLAUDE.md` §4 locks a stack but names no versioning library, and R3 makes the versioned
REST API the product's real surface — everything the desktop shell, the Android app and any future
integration ever sees. The obvious choice is `Asp.Versioning.Http`, which brings media-type and
query-string versioning, deprecation and sunset policies, version sets and a version-aware OpenAPI
provider.
**Decision.** URL-segment versioning only — `/api/v1/...` — implemented by `VumaApi.MapVumaApi()` in
about fifty lines, plus an `X-Vuma-Api-Version` response header. Un-versioned routes are a closed,
named list (`/health`, `/openapi/*`) enforced at runtime against the endpoint table.
**Consequences.** The same reasoning as ADR-009: a library's model of versioning would become the
shape of the API for the next thirty stages, and Vuma has not decided it wants media-type versioning
or a deprecation policy. The path is legible in a log, in a proxy rule and in a support call, which
matters when the person reading it is a shop's IT contractor. The header exists because a store with
a reverse proxy in front of the server may see a rewritten path. Cost: if Vuma ever wants
header-based versioning it is a new decision and a rewrite of this file, which is the right price
for not having guessed.

## ADR-044 — The pipeline owns the transaction; handlers do not commit — **LOCKED**
**Context.** Stage 01's `IUnitOfWork` XML doc has always said "the Stage 03 command pipeline opens
and commits it; handlers do not". Stage 02 shipped eight handlers that each took `IUnitOfWork` and
called `CommitAsync`, because no pipeline existed yet. `PROGRESS.md` left the choice open: let
handlers keep committing and wrap them in a transaction that no-ops when nested, or take the commit
away from them.
**Decision.** Take it away. A handler mutates tracked entities and returns; `TransactionBehaviour`
opens the transaction and commits once around it. Handlers do not take `IUnitOfWork` at all, and two
architecture tests enforce it — one on the constructor dependency, one on the `CommitAsync` call
itself. `AuthenticationService` is the single exemption and is deliberately not a handler (ADR-040);
the demo seeder is exempted for the two platform rows it writes directly, because no command creates
a tenant or a store until Stage 06.
**Consequences.** ADR-006 becomes implementable: the outbox row a change produces lands in the same
transaction as the change, which is impossible if the change may already have committed. A command
that does two things can no longer half-apply. Queries never open a transaction at all, which keeps
ADR-028's promise that reporting stays cheap while a tenant is read-only.
The rule paid for itself the day it was written: it found that `DemoSeed` resolved command handlers
directly, so with the commit removed the seed would have run to completion, printed an activation
code, and persisted nothing. It now goes through the dispatcher.

## ADR-045 — JWT inbound claim mapping is off — **LOCKED**
**Context.** `JwtBearerOptions.MapInboundClaims` defaults to `true`, which rewrites standard JWT
claim names into the WS-Federation URIs of the .NET 4.5 era — `sub` becomes
`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`. Stage 02 issues `sub` and
reads `sub`, in the endpoints and in `PermissionAuthorizationHandler`.
**Decision.** `MapInboundClaims = false`. Claims are read under the names they were issued with,
which are the JWT registered names and Vuma's own `vuma:*` types.
**Consequences.** This was not a preference; it was a live bug. Every `FindFirstValue("sub")`
returned null, so `GET /api/v1/me/permissions` answered `401` on a perfectly valid token and every
permission-gated endpoint answered `403` for a user who genuinely held the permission. Stage 02's
tests all ran at the service level and none of them went through the JWT bearer handler, so nothing
caught it. Stage 03's `WebApplicationFactory` tests did, on the first run — which is the argument for
`docs/TESTING.md` §1's API level existing at all, made concrete.

---

# Revision 7 — Stage 04 sync and cloud backup

## ADR-046 — The audit stamp is applied before the outbox serialises, not at SaveChanges — **LOCKED**
**Context.** Stage 01 put audit stamping in `AuditInterceptor`, which runs at `SavingChanges`. Stage
04 put outbox capture in pipeline slot 300, which runs after the handler and *before* the commit.
Both were individually right and the combination was wrong: the payload was serialised before the
stamp existed, so every replicated row left its origin node carrying `created_by = ""` and
`created_at = 0001-01-01`. Because those two columns are written once and never again, the receiving
tier stored the blanks permanently. R6 — "who changed what, when" — was satisfied only on the machine
where the change was made, which is the one place nobody has to ask. It was found by the
`sync-and-offline` agent and confirmed by dumping a real payload.
**Decision.** The stamping moves into `AuditStamper`, a scoped service shared by the outbox behaviour
and the interceptor. The behaviour stamps before it serialises; the interceptor stamps whatever the
behaviour did not reach — a background job, the seeder, a query-side save — and stamping the same
entity twice in one save stamps it once. A row applied from a peer is **not** re-stamped: it arrives
with the originating node's principal and instant, and `IReplicationScope.AppliedRowIds` is how the
stamper knows. The audit *entry* written for that same save still records this node's own principal.
**Consequences.** The row says who changed the record; the trail says what this machine did about it.
Both questions get asked in an investigation and they now have different, correct answers. The cost
is that stamping is no longer in exactly one place — it is in one class called from two, which is the
smallest arrangement that can be correct given where the two components sit in the pipeline. The
hard-delete rewrite moved with it, which is a bonus: the outbox's delete detection and the audit
trail's now agree by construction rather than by two implementations matching.

## ADR-047 — A bad operation is rejected alone, never with its batch — **LOCKED**
**Context.** The sync receiver applied a batch with no per-operation error handling, inside one
transaction. A single unparseable payload therefore rolled back every operation in the batch. That
would be survivable if the batch were retried differently next time — but `ListDueAsync` re-selects
the same oldest-stamped rows every pass, so the poisoned operation is in every subsequent batch too.
One bad row would stop a store replicating for ever, and the only symptom would be a queue depth that
climbs and never falls.
**Decision.** `DomainException` is caught per operation and recorded as a `Rejected` outcome for that
operation, with its code and message on the inbox row. Nothing else is caught: a dropped connection,
a deadlock or a timeout is a statement about the batch rather than about one operation, and must roll
the whole thing back so the sender retries — swallowing it would acknowledge operations this node
never applied.
**Consequences.** A peer with a bug gets told which operation is wrong and why, and everything else
keeps flowing. The distinction the catch relies on is the one `DomainException` already draws
(`CONVENTIONS.md` §5): expected refusals carry a stable code, infrastructure failures propagate.
Stage 04 leaves one related case ungraceful and documented rather than fixed — two genuinely
concurrent deliveries of one operation fail the racing batch on the inbox unique index instead of
answering `Duplicate`. It is self-healing and cannot double-apply, and making it graceful needs a
savepoint per operation, which is a real per-batch cost to smooth a rare path.

## ADR-048 — `VumaRetail.Sync` sits below `Infrastructure`, not beside it — **LOCKED**
**Context.** `CLAUDE.md` §5 lists `VumaRetail.Sync` alongside `Infrastructure`, and `LayeringTests`
treats them as peers. Stage 04 had to put the replication protocol — the hybrid logical clock, the
conflict resolver, the receiver, the dispatcher — somewhere. Beside Infrastructure it would need its
own adapters or a sibling reference; inside Infrastructure it would be indistinguishable from the EF
code it is supposed to be independent of.
**Decision.** `VumaRetail.Sync` references `Application` and `Contracts` only — no EF, no HTTP, no
S3 — and `Infrastructure` references *it*. Infrastructure supplies the adapters: the EF replica
writer, the HTTP transport, the vaults, the DI wiring.
**Consequences.** The whole protocol is testable against an in-process transport with no second host
in the room, which is what made the conflict matrix and the dispatcher's backoff assertable at all.
The same protocol runs on a terminal, a store server and the cloud with only `NodeKind` differing.
The cost is one more layer than §5 describes, in the same spirit as ADR-031 and ADR-039 — the layout
there is the finished shape, and this is how it is reached.

## ADR-049 — The hybrid logical clock stamp is a base-entity column — **LOCKED**
**Context.** ADR-007 says "every replicated row carries an HLC stamp". Nothing did. `CLAUDE.md` §7
rule 3's mandatory column list does not name one, so the obvious reading was that the stamp lives on
the outbox row only. That does not work: the receiver has to compare an inbound change against *the
row it already has*, and a stamp that exists only on the sender's outbox is not available to it. Every
conflict policy that depends on order — which is most of them — would degenerate into "whatever
arrived last", which is the behaviour ADR-007 exists to prevent.
**Decision.** `Entity.SyncStamp` joins the mandatory columns, mapped once in `EntityConfiguration<T>`
as the sortable string form. It is written by the outbox behaviour on a local change and by the
replica writer on a remote one — in the second case with the *sender's* stamp, never a fresh local
one, because a receiver that re-stamped would make every applied change look newer than the original
and the next comparison in the other direction would undo it.
**Consequences.** One migration touching every existing table, which is cheap now and would not be
later. The stamp is excluded from the audit diff alongside `row_version` and `sync_state` — it changes
on every write and would bury the column that actually changed. It is deliberately **not** a
timestamp and no business rule may read it as one: when something happened is `occurred_at`, from
`IClock`.

---

# Revision 8 — Stage 04b licensing, activation and entitlement

## ADR-050 — Ed25519 comes from BouncyCastle — **LOCKED**
**Context.** `docs/LICENSING.md` §2 and ADR-026 both name Ed25519 for licence, lease and emergency-code
signatures, verified against a public key pinned into the binaries. .NET 9's
`System.Security.Cryptography` has no Ed25519 primitive, so it has to come from somewhere.
**Decision.** `BouncyCastle.Cryptography` — MIT-licensed, pure managed, no native dependency.
**Consequences.** The alternatives were worse for this product rather than merely different. NSec is
faster and wraps libsodium, which means a native binary per architecture inside a WPF till and a WiX
bundle — a deployment problem on the one platform that matters. Substituting ECDSA P-256 would avoid
the dependency and would also mean overturning a written decision to save one package, which is the
wrong trade for the component that decides whether a customer's software runs. The signing half
(`LicenceSigner`) exists for the control plane, the in-process fake and the tests; a store server only
ever verifies, and in production the private key lives in a KMS and is never in a process at all
(ADR-024).

## ADR-051 — `VumaRetail.Licensing` sits below `Infrastructure`, and ships a development key pair — **LOCKED**
**Context.** Stage 04b needed a home for the enforcement ladder, the signed-document format, the
entitlement choke point and the read-only guard. Putting it in `Infrastructure` would tie the ladder to
EF Core; putting it in `Application` would put a crypto dependency in the layer every module compiles
against.
**Decision.** A new project on ADR-048's pattern: `VumaRetail.Licensing` references `Application` and
`Contracts` only — no EF, no HTTP, no file system — and `Infrastructure` references *it* and supplies
the adapters. It also carries `InProcessControlPlane`, a working control plane (ADR-022's shape), and a
**development Ed25519 key pair derived from a seed compiled into the assembly**. The store server
refuses to start on that key outside Development, exactly as it does for the placeholder JWT signing
key.
**Consequences.** Both ladders, every boundary, the counter checks and the whole no-accidental-lockout
suite are testable as arithmetic on a clock the test moves by hand, with no vendor service in the room
and — for the ladder itself — no database. The shipped key pair is the thing that makes a demo work on
a laptop on a plane and CI work with no secrets; it is also a master key for anybody who uses it in
production, which is why the refusal to start is a refusal rather than a warning.

## ADR-052 — The read-only exemption set stays three kinds; membership is a closed list — **LOCKED**
**Context.** ADR-034 capped the ADR-028 carve-outs at three — payment, offline flush, backup — and the
Stage 00 architecture test enforced that by counting *commands*, not kinds. Stage 04 had already used
all three (two backup commands and one sync receive), so the next legitimate carve-out would have
failed the build. Stage 04b then needed several: every button on the licence screen is a write, and a
screen that stops working during a lapse is a screen nobody can use to end one.
**Decision.** The cap is on **kinds**, which stays at three, and membership is a **closed list asserted
by name** — the same shape as Stage 04's `BypassTenantFilter` call-site list. `ReadOnlyExemption.Payment`
is read as "the commercial relationship itself": the payment page, the card-update flow, the licence
screen and everything on it (activate, rebind, retry now, redeem an emergency code, heartbeat), and
**withdrawal of a vendor support-access grant**. Granting a *new* support grant stays an ordinary write.
**Consequences.** The exemption stays reviewable in one place and a new member costs a test edit and a
sentence saying why, rather than a code review nobody remembers. Consent moves only in the direction of
less access while a tenant is restricted: they cannot let the vendor in, and they can always put them
out. The counting test was wrong the moment Stage 04 shipped and would have failed on the fourth
carve-out whatever it was; correcting it here is a correction, not a widening.

## ADR-053 — Fingerprint tolerance scales with what the machine can report — **LOCKED**
**Context.** `docs/LICENSING.md` §3 fixes the hardware match at 7 of 11 weighted points, so that
replacing a network card and a data disk leaves a licence bound. Two of the five components — the
motherboard UUID and the Windows machine GUID — come from WMI and the registry, which this
cross-platform build cannot read (ADR-031) and which Stage 31 supplies. Held to an absolute seven, a
machine that can only report six or eight points would demand a rebind for a single replaced NIC, which
is the exact failure the tolerance exists to prevent.
**Decision.** The threshold is 7 of 11 when all five components were captured, and the same
*proportion* of whatever was captured when they were not. The documented case is unchanged and exact.
**Consequences.** A weaker binding is weaker in the tolerant direction rather than the brittle one,
which is the correct way for this to fail — a false rebind demand stops a paying customer trading, and
ADR-026 already chose detection over prevention. A missing component still scores nothing, so absence
never reads as agreement, and no value is ever fabricated to fill a gap.

## ADR-054 — `CurrentLevel` moves off `IEntitlementService` onto `IEnforcementStatusReader` — **LOCKED**
**Context.** `IEntitlementService`'s own remarks promised an architecture test asserting that no query
handler consults the enforcement level (ADR-028 rule 4: a lapsed tenant keeps full read, report,
reprint and export access, so a read path has no correct use for the gate). Writing that test exposed
that the promise did not hold: `GetLicenceStatusQueryHandler` and `GetSubLeaseQueryHandler` — the
licence screen and the till's sub-lease, both queries — already depended on `IEntitlementService` and
called `CurrentLevel` to display it. Both uses are legitimate; the licence screen exists to show the
level. A named-exception list (Stage 04's `BypassTenantFilter` call-site pattern) would have made the
rule true in the same reviewed-list sense ADR-052 already uses for exemption kinds, but this codebase's stronger
habit — layering, `CommitAsync`, the pipeline's `IUnitOfWork` ban — is to make an invariant a type
cannot violate rather than a list a reviewer must remember to check.
**Decision.** `CurrentLevel` moves onto a new interface, `IEnforcementStatusReader`, implemented by the
same `EntitlementService` instance and resolved to it within a scope so the gate and the status reader
never disagree about where a request's tenant stood. `IEntitlementService` keeps only
`IsModuleEnabledAsync` and `CheckLimitAsync` — the gate proper. Everything that only needs to *report*
the level (the read-only guard, `RefreshLeaseCommand`, `SendHeartbeatCommand`, the two query handlers)
now depends on `IEnforcementStatusReader` instead. "No query handler depends on `IEntitlementService`"
is therefore enforceable with no exemption list at all: there is no `CurrentLevel` on that interface for
a query handler to plausibly justify calling.
**Consequences.** The architecture test in `LicensingRulesTests.cs` needs no named exceptions and cannot
go stale the way a hand-maintained list can. The split cost five call sites their parameter type and
nothing else — no behavioural change, since both interfaces are the same object within a scope. A
future query handler that wants the enforcement level for display depends on
`IEnforcementStatusReader`, which is safe by construction; one that wants to gate a read on it has no
interface that lets it, which is the point.

---

# Revision 3 — ecosystem, savings schemes, design system, unattended operation

## ADR-055 — Money held for customers is a liability with its own module — **LOCKED**
**Context.** Credit accounts, lay-bys and stokvels all involve holding a customer's money or goods
before delivery, and all three are commonplace in South African retail. Bolting them onto Sales would
produce three inconsistent implementations of the same accounting problem.
**Decision.** One module (Stage 10b). Deposits, contributions and account credits post to **liability**
accounts through Stage 07's posting rules and release to revenue only on delivery. Lay-by stock is
reserved, not sold — no revenue, no stock issue, no cost of sale until final payment. Stokvel member
balances are a projection of an append-only contribution ledger; there is no "set balance" operation.
Stokvel benefits are allocated **time-weighted** by what each member contributed and when.
**Consequences.** The three liability balances reconcile to their control accounts continuously, like
every other sub-ledger. The time-weighted benefit calculation is the most dispute-prone arithmetic in
the product and carries a hand-computed fixture test. Reserved lay-by and stokvel stock is excluded
from available stock, so a store cannot sell December's hampers in November.

## ADR-056 — Vuma Connect: both sides are tenants, and a supplier never writes into a retailer — **LOCKED**
**Context.** The product becomes a network when suppliers publish prices once and connected retailers
order and pay in-app. The obvious build — a supplier "portal" bolted onto the retailer's system — breaks
immediately, because a wholesaler is a supplier to twenty retailers *and* a retailer buying from ten
manufacturers.
**Decision.** A supplier is a full Vuma tenant. Trading happens over mutually accepted, revocable
connections, and **every cross-tenant data flow is a proposal the receiving side accepts** — published
prices land in the retailer's import preview with a diff, never as a silent cost update. The one
concession is that a dispatch note pre-populates a GRN, which the retailer still confirms line by line.
Connection codes issued by suppliers are the growth mechanism.
**Consequences.** Retailers will connect, because a supplier can never change their costs behind their
back. Cross-tenant isolation becomes the highest-stakes security surface in the product and gets a
dedicated suite: no retailer sees another's volumes, no supplier sees a competitor's prices, and network
benchmarks are suppressed where too few tenants would make a figure attributable.

## ADR-057 — Vuma orchestrates payments, it does not hold funds — **LOCKED**
**Context.** In-app payment between retailer and supplier looks like the natural centre of the network,
and it is also the fastest way to acquire regulatory obligations nobody planned for.
**Decision.** Money moves through a licensed provider behind `IPaymentGateway` and `ISettlementProvider`.
Vuma orchestrates, records and reconciles: a payment posts to the retailer's AP and the supplier's AR in
one atomic operation. Both interfaces ship with fakes so Stage 21b is fully testable without a licence,
and the regulatory position is documented in `docs/compliance/` before any real funds flow.
**Consequences.** The network's value — one order, one status, one reconciled payment — is delivered
without Vuma becoming a payments institution. If the model later requires holding funds, that is a
provider change and a licence, not a rewrite.

## ADR-058 — One design system, generated from one token file — **LOCKED**
**Context.** Four surfaces (WPF desktop, Android, supplier portal, storefront) drawn by different stages
months apart will diverge unless the tokens are mechanical.
**Decision.** `design/tokens.json` is the single source of truth. WPF `ResourceDictionary`, Compose
theme and CSS custom properties are **generated** in CI; a hand-edited generated file fails the build,
and an architecture test scans for literal hex values in any UI source. Dark and light are both
first-class, with dark defaulting on POS and light in back office, overridable per user and per
terminal. Tenant branding is limited to logo, accent and receipt header.
**Consequences.** Every Vuma install is recognisable and supportable. Contrast is verified in CI, so a
palette tweak cannot silently break legibility on a five-year-old till screen.

## ADR-059 — Unattended operation is a first-class requirement — **LOCKED**
**Context.** Build sessions run overnight with nobody watching. A single permission prompt or clarifying
question wastes the entire night.
**Decision.** `AskUserQuestion` is denied in settings. `CLAUDE.md` forbids writing to `.claude/**`
(a protected path whose writes prompt). Bypass mode is set at launch from both user and project scope.
`scripts/run-autonomous.ps1` runs **one stage per invocation** — fresh context each time — and verifies
three independent progress signals (a commit landed, `PROGRESS.md` changed, the build is green) before
continuing, stopping cleanly rather than looping.
**Consequences.** Quality does not decay across a long night the way it does in one enormous session,
and a stalled run stops with a diagnosable log instead of burning hours. See `docs/AUTONOMOUS_OPERATION.md`
for the environment setup this depends on.

---

# Revision 9 — Stage 06 master data

## ADR-060 — A variant's attributes are one ordered `jsonb` column, not a child table — **LOCKED**
**Context.** `ItemVariant` needs a small, ordered set of named attribute pairs — `Size: M`, `Colour:
Red` — and the number of dimensions a retailer actually uses is almost always one or two, occasionally
three, and essentially never open-ended. A child table (`item_variant_attributes`) is the conventional
normalised shape and is also a join on every variant read, a migration to add an index the first time
somebody asks "find every red thing", and a second sequence column to keep display order, which a
relational table does not give for free.
**Decision.** `ItemVariant.Attributes` is an ordered `IReadOnlyList<VariantAttribute>` value object,
mapped to a single `jsonb` column (`CatalogConfigurations.ItemVariantConfiguration`). Order is
preserved because it is display order and a JSON array keeps it without an extra column. A value
comparer is registered so EF's change tracker treats two lists with the same elements in the same
order as equal, which is what makes the column participate correctly in `SaveChanges` and in the
mapping-conformance round-trip tests.
**Consequences.** Reading a variant costs nothing beyond the row it already fetches, and adding a
fourth attribute to the product line is a data edit, not a migration touching every existing variant
row. The cost is paid on the other side: attributes are not individually indexable or queryable in SQL
without a `jsonb` expression index, which nobody needs today — POS and catalogue search resolve a
variant by SKU or barcode, never by filtering on `Colour = 'Red'` across the whole catalogue. If that
requirement ever arrives, promoting the column to a child table is a self-contained migration, not a
domain redesign, because `VariantAttribute` is already a named pair rather than a free-text blob.

## ADR-061 — A barcode is the one entity in this stage that is genuinely hard-deleted — **LOCKED**
**Context.** CLAUDE.md §7 rule 8 makes soft delete the default for every table, and `Item`,
`ItemVariant`, `Partner` and `UnitOfMeasure` all follow it — each carries a `Deactivate()`/`Activate()`
pair instead of a delete. A barcode is different in kind: it identifies a product to a scanner and
carries no history of its own the way a sale line, a stock movement or a partner does. A shop
relabelling a product with a new EAN has no use for the old code sitting in every future lookup,
uniqueness check and keyset page forever.
**Decision.** `RemoveBarcodeCommand` calls `IBarcodeRepository.Remove`, which is a real
`DbSet.Remove` — the row is gone, not `deleted_at`-stamped. Removing a primary barcode promotes a
remaining sibling atomically in the same handler (`RemoveBarcodeCommandHandler`), so the "exactly one
primary" invariant survives the deletion.
**Consequences.** A barcode value becomes reusable the instant it is removed, which is correct — the
uniqueness index is filtered on `deleted_at IS NULL` for every other catalog table but a removed
barcode leaves no row at all, so there is nothing to filter. The replication registry still declares
`Bidirectional`/`CloudWins` for `Barcode` (`docs/SYNC_AND_BACKUP.md` §3): a delete is itself a change
the outbox captures and ships like any other, and `CloudWins` decides which node's delete-or-recreate
wins if both edit the same barcode's owner at once. The one thing this ADR rules out is ever adding a
`deleted_at` column to `catalog.barcodes` to "make it consistent" with its siblings — that would be
solving a problem (audit of a removed alias) nothing in this product asks for, at the cost of the
uniqueness index every add-barcode call depends on.

## ADR-062 — Item and partner counts get no `LimitKind`; they stay unlimited — **LOCKED**
**Context.** `docs/LICENSING.md` §6 fixes the hard limits — stores, terminals, named users — and the
soft limits — transactions/month, storage, API calls — as closed lists, and says plainly that a new
kind is not to be added speculatively. Stage 06 is the first stage that could plausibly want one: a
catalogue of items and a register of partners are exactly the kind of thing a SaaS pricing tier
sometimes gates on ("up to 5,000 SKUs").
**Decision.** No `LimitKind.Items` or `LimitKind.Partners` is added. A tenant may create as many items,
variants, barcodes and partners as their catalogue needs, regardless of plan.
**Consequences.** This is a commercial decision as much as an engineering one, and it is made here
because nothing downstream should invent a limit to fill a perceived gap: `docs/LICENSING.md` names the
complete list, and master data is deliberately not on it. The reasoning is the same ADR-034/ADR-052
pattern applied to a different list — a closed enumeration is only trustworthy if additions are
deliberate and recorded, not incidental to whichever stage happens to touch it next. If the vendor ever
wants to sell by catalogue size, that is a new ADR revising `LICENSING.md` §6, not a quiet addition
here. Until then, `CreateItemCommandHandler` and `CreatePartnerCommandHandler` check only code
uniqueness and an active unit of measure — no `IEntitlementService.CheckLimitAsync` call — which is the
same shape `CreateStoreCommandHandler` uses for the hard limit it *does* have, so the absence here is a
deliberate omission rather than one nobody thought about.

## ADR-063 — The daily reconciliation check is a plain `IHostedService`, not a Quartz job — **LOCKED**
**Context.** ADR-016 requires an "automated daily job" that compares every control account against its
sub-ledger, so a variance is visible before somebody tries to close a period rather than only at the
close attempt. `CLAUDE.md` §4 locks Quartz.NET as the scheduler for background work, which reads as an
instruction to make this a Quartz job.
**Decision.** `FinanceReconciliationHostedService` is a `BackgroundService` on a 24-hour loop. It
issues `RunDailyReconciliationCheckCommand` through the ordinary command pipeline — validation,
transaction, audit — exactly as a person clicking "check now" would.
**Consequences.** Quartz earns its place when schedules are tenant-configurable, need persistence
across restarts, or must not double-fire across a cluster. None of those apply: the interval is fixed
at 24 hours, a missed pass costs nothing because the next one recomputes from current state rather
than from a delta, and the store server is a single node by topology. Introducing a scheduler, its
tables and its own transaction semantics to run one idempotent command on a timer would be more moving
parts than the requirement has. The important half of the decision is not the timer but that the
scheduled path and the on-demand path are the same command — a check that behaved differently when a
person asked for it than when the clock did would be the one worth having, and this way there is only
one. When a later stage genuinely needs tenant-configured schedules, Quartz arrives then and this
service becomes one of its jobs without the command changing.

## ADR-064 — Finance is a core module, not a licensable one — **LOCKED**
**Context.** R7 makes every module switchable per tenant, and `FinanceModuleManifest` has to declare
whether it is core. Finance is a plausible upsell — plenty of retail products sell "accounting" as a
tier.
**Decision.** `IsCore => true`. The `finance` licence flag exists and is declared, but the module
cannot be switched off.
**Consequences.** Rule 12 makes Finance load-bearing for every other module: POS, procurement and
inventory all move value, and the only way any of them may affect the ledger is by raising an
`IFinancialEvent` that Finance turns into a journal. A tenant with Finance disabled would not lose a
feature the way a tenant without Loyalty does — every posting write in the system would fail,
including a sale at the till, which contradicts R1's "POS never stops" outright. This is the same
judgement Stage 06 made for Catalog and Partners, one layer lower: those are core because other
modules read them, Finance is core because other modules cannot write without it. Selling accounting
as a tier remains possible commercially — it would gate the *UI surface* and the AR/AP/banking
endpoints via permissions, not the posting engine underneath — but that is a pricing decision for the
control plane, and it must never reach the code path a POS sale takes.

## ADR-065 — Document number counters are node-local and locked inside the caller's transaction — **LOCKED**
**Context.** Journals, AR invoices and AP payments all need per-tenant sequential human-readable
numbers. Two questions follow: whether the counter replicates, and how two concurrent postings avoid
issuing the same number.
**Decision.** `DocumentNumberCounter` is `ReplicationScope.NodeLocal`. The repository takes a
`SELECT ... FOR UPDATE` row lock inside the command pipeline's existing transaction (slot 200) rather
than opening one of its own.
**Consequences.** Replicating the counter would be actively harmful: the number is printed on the
document before any replica could learn about it, so a replicated counter converging after the fact
would hand two stores the same invoice number and then quietly agree on which one was right. Keeping
it node-local means a store's series is that store's series, which is what a person reading an invoice
number expects anyway. Taking the lock inside the caller's transaction — not a fresh one — is what
makes the series gap-free rather than merely unique: the increment rolls back with the command that
failed, so a rejected posting does not burn a number. It also has to be that way regardless, since
§7 rule 2 and the architecture test forbid a repository committing its own `SaveChanges`. The cost is
that concurrent postings in the same series serialise on that row; for a store issuing tens of
documents a minute this is invisible, and a store that ever outgrows it wants a different numbering
scheme rather than a looser lock.

## ADR-066 — `Journal.Lines` is a backing-field one-to-many, not an owned collection — **LOCKED**
**Context.** A journal's lines are created once, at `Journal.Post`, and never added to afterwards.
That looks exactly like an owned collection, which is how EF would normally model a child collection
with no independent lifetime.
**Decision.** `JournalLine` is a full entity with its own configuration; `Journal.Lines` is mapped as
the parent side of a plain one-to-many over a backing field, with `PropertyAccessMode.Field`.
**Consequences.** An owned collection would deny the line its own identity, and the line needs one for
three separate reasons: it carries the standard audit columns §7 rule 3 requires on every table, it is
independently `[Replicated]` with its own sync stamp, and `BankStatementLine.MatchedJournalLineId`
points at a specific line — a bank reconciliation matches one side of one posting, not a whole
journal. Owned types also cannot be queried independently, and the trial balance and account-balance
queries both group by account across every journal without loading the parents. The backing field is
what preserves the invariant that motivated considering ownership in the first place: the collection
is populated inside `Post` and there is no public way to add to it afterwards, so immutability is
enforced by the aggregate rather than by the mapping.

## ADR-067 — An optional `Money` is two plain columns and a computed accessor, never a complex property — **LOCKED**
**Context.** A journal line carries a debit or a credit, never both and never neither, so each side is
optional on its own. `ValueObjectMapping.HasMoney` maps `Money` as an EF complex property, and the
obvious move was a second overload taking `Expression<Func<TEntity, Money?>>`.
**Decision.** There is no `HasMoney` overload for `Money?`. An entity with an optional monetary amount
declares `{name}Amount` and `{name}Currency` as plain properties and exposes a computed `Money?`
accessor over them; `JournalLine` is the worked example, and its mapping sets the column type and
length directly.
**Consequences.** This one was learned the expensive way and is recorded so nobody re-derives it. EF
Core 9 cannot configure a complex property as optional at all, and reaching a nullable value type's
fields requires the two-hop expression `value!.Value.Amount`, which `ComplexPropertyBuilder.Property`
refuses to parse. Both failures happen at *model-building* time, not compile time — so the overload
compiled cleanly, shipped in a checkpoint commit, and left a solution in which `VumaRetailDbContext`
threw on construction. Every integration test and every architecture test that reads the model failed,
and a build with zero warnings said nothing was wrong. The general lesson is the one worth keeping:
`dotnet build` proves nothing about an EF model, so a stage that adds entities is not green until
something has actually constructed the context. `HasAddress` solves the same EF limitation the other
way, with an owned type, because an address has no arithmetic to preserve and does not mind losing
value-type semantics; money does, so it keeps `Money` in the domain and pushes the split into the
mapping alone.

---

## ADR-068 — Stock is valued at weighted-average moving cost — **LOCKED**
**Context.** Stage 08 has to put a number on what a unit of stock cost when it is relieved, and the
choice governs every cost of sales figure the ledger will ever carry. FIFO, specific identification,
standard costing and weighted average are all defensible; the roadmap's later stages (12 procurement,
17 manufacturing) each assume *something* is already decided.
**Decision.** Weighted-average moving cost. Every receipt blends its cost into a running average held
on `StockBalance.AverageCost`; every issue relieves at that average without changing it. There are no
cost layers, no lot tracking and no revaluation pass.
**Consequences.** This is the simplest method that is still correct, which is what `CLAUDE.md` §1 asks
for on a first cut. It needs one decimal per balance rather than a layer table, it cannot get out of
order, and it has no batch job that must run before the numbers are true. What it gives up is real:
a tenant whose accountant requires FIFO cannot have it, and shelf-life or serial-number costing
(Stage 18's territory) will need genuine cost layers, not a variation on this. That is an additive
change — a costing-method column on the item and a layer table beside the balance — rather than a
rewrite, because every posting already goes through `StockLedgerPoster` and nothing else computes a
cost. Note the deliberate detail that an emptied balance keeps its last average: a positive adjustment
or a found-stock variance afterwards then has a cost basis, where zeroing it would force the caller to
invent one.

---

## ADR-069 — `StockBalance` is node-local and rebuilt, never replicated as a value — **LOCKED**
**Context.** ADR-005 makes the stock ledger append-only and names `stock_balance` a materialised
projection. Every other replicated table carries a `ReplicatedAttribute` naming a conflict policy, and
the obvious reading is that the balance replicates too, `LastWriterWins` like any other mutable row.
**Decision.** `StockBalance` is `ReplicationScope.NodeLocal`. Ledger entries replicate
(`StoreToCloud`, `AppendOnly`); the balance does not. Each node maintains its own copy from the
entries it has applied, whether raised locally or received from a peer.
**Consequences.** A running total is not safely mergeable the way an accumulating ledger is. Two nodes
each applying their own delta to a synced total would either double an overlapping movement or
silently diverge, and neither shows up as a conflict for ADR-007 to escalate — it shows up months
later as stock figures nobody trusts. Keeping the balance local means the only thing crossing the wire
is the immutable fact that a movement happened, which merges by accumulation and cannot conflict. The
cost is that a node's balance is only as current as the entries it has received, and a rebuild is the
repair for any divergence — which is available precisely because the ledger is append-only and
complete.

---

## ADR-070 — A missing posting rule does not refuse a stock movement — **LOCKED**
**Context.** Stage 08 raises an `IFinancialEvent` for every movement and Stage 07's engine resolves a
`PostingRule` for its event type, throwing `PostingRuleNotFoundException` when none is configured. The
question is what inventory does with that exception.
**Decision.** `FinancialInventoryValuationEventPublisher` catches it, logs a warning naming the event
type and the ledger entry, and returns. The stock movement stands; no journal is raised.
**Consequences.** The stock ledger records what physically happened, and that is true whether or not
an accountant has finished the chart of accounts. Failing the movement would mean a store cannot
receive a delivery because its posting rules are incomplete — refusing to write down what is
demonstrably on the shelf, which is the wrong failure. It also makes the transfer case fall out
correctly rather than needing a special case: with a single inventory account a transfer's two sides
would debit and credit the same account for the same amount, so the demo seeds no transfer rule and
the correct posting is no posting. The real risk is the other reading — a rule that *should* exist and
does not, silently costing the ledger a journal — which is why it is a warning rather than debug, and
why the message names the event type a rule would need. A stage that needs the stricter behaviour
(Stage 12's three-way match is the likely first) should make it explicit per event type rather than
flipping this globally.

---

## ADR-071 — A constructor overload never defaults a parameter that differs only by nullability — **LOCKED**
**Context.** `Entity` gained a second constructor for callers that mint an id before the row exists —
`StockTransfer`, whose two ledger entries must carry the transfer's id before the transfer itself can
be built. It was written as `Entity(Guid id, Guid tenantId, Guid? storeId = null)`, beside the
existing `Entity(Guid tenantId, Guid? storeId = null)`.
**Decision.** The id-taking constructor takes no default for `storeId`. All three arguments are
required. More generally: where two overloads differ by a leading parameter of the same type, the
longer one does not get a trailing default that makes both applicable to the same argument count.
**Consequences.** With the default present, a two-argument `base(tenantId, storeId)` call from an
entity whose own `storeId` is a non-nullable `Guid` binds to the *id* constructor — two exact `Guid`
matches beat one exact match plus a `Guid`-to-`Nullable<Guid>` conversion. C# is behaving exactly as
specified; the result is a row with `Id` set to the tenant's id, `TenantId` set to the store's, and no
store at all, which places it outside its own tenant's global query filter. Nothing about the call
site changes, no warning is produced, and the build stays clean. `Terminal` was the only entity in the
solution shaped this way and it cost fourteen failing tests across Identity, Licensing and Sync, none
of which named the real cause. Removing the default makes the two-argument call unambiguous again.
This sits beside ADR-067 as the second case this codebase has hit where the compiler is content and
the model is wrong; `EntityTests` now asserts both constructors bind as intended, because that is the
only place the mistake is visible.

---

## ADR-072 — A sale line carries the price it was sold at; POS does not resolve prices — **LOCKED**
**Context.** Stage 09 builds the till, and a till has to put a number on a line. Stage 06's own scope
note says it ships "no price, cost or promotion — that is Stage 10", so there is no price list on
`main` to read from, and `ROADMAP.md` puts the price list, the promotions engine and the specials
engine together at Stage 10. POS could have built a minimal price table to get itself unblocked.
**Decision.** `SaleLine.UnitPrice` is supplied by the caller and stored as sold. POS resolves what is
being sold (`SellableItemResolver`, through catalog's published ports) and what tax applies to it
(`ITaxCalculator`, through Finance's), and resolves nothing about price. `AddSaleLineCommand` takes
the unit price and an optional manual discount as inputs.
**Consequences.** A minimal price table built here would be the second place a price lives the moment
Stage 10 arrives, and the migration off it would have to rewrite completed sales — which are
immutable — or leave two pricing paths in the product permanently. It also would not have been
sufficient: a weighed line, an open-price line and a manually reduced line all have a price that no
list could have supplied, so the "price arrives with the line" path has to exist regardless. Stage 10
changes what fills the field in, not the field. The cost is that until Stage 10 the caller is
responsible for the number, which for the demo seed means a literal and for a real till means the
screen the operator is looking at; nothing about that is wrong, it is just not yet automatic. The
line stores its computed tax alongside, so a rate change tomorrow does not restate a receipt a
customer is holding.

---

## ADR-073 — A stock issue the ledger refuses does not fail the sale — **LOCKED**
**Context.** Two rules meet at the moment a sale completes. R1: POS never stops. Stage 08 rule 4
(ADR-005): stock cannot go negative through any path. When the shelf and the system disagree — which
in retail is routine, not exceptional — the customer is standing there holding an item the ledger says
does not exist, and something has to give.
**Decision.** The sale completes. The stock issue is attempted per line through Stage 08's
`IStockLedgerPoster`; if inventory refuses it, the line is marked `StockIssueStatus.Refused` with the
refusal's own message, no ledger entry is written, and completion carries on.
`ListRefusedStockIssuesQuery` and `GET /pos/reconciliation/stock-issues` are the queue a manager works
through, and `SaleCompletionResult.StockIssuesRefused` tells the till it happened at the moment it
happened. Any *other* failure — a database fault, a cancellation — propagates and rolls the command
back.
**Consequences.** Neither rule is weakened: nothing posts a negative balance, and no customer is
turned away. The alternative readings are both worse. Refusing the sale means a shop stops trading
because its stock data is stale, and the goods leave the building anyway. Forcing the posting through
means Stage 08's central invariant is true except when POS says otherwise, which is not an invariant.
The discrepancy is recorded as a queryable row rather than a log line specifically because somebody
has to reconcile it, and nobody reconciles from a log. A growing queue is a real signal — it means the
shelf and the system have drifted apart and a stocktake is due — which is information the previous two
options destroy. The cost is that a sale can complete having relieved nothing, so cost of sales for
that line is not posted either; that is the honest state of affairs and it is visible, which is the
most this stage can offer without inventing stock that is not there.

---

## ADR-074 — Stage 10 is split; quotes, invoices and sales analytics become Stage 10c — **LOCKED**
**Context.** `ROADMAP.md` lists five features against Stage 10: quotes, invoices, returns, the
specials engine and sales analytics. That is not one focused session, and the roadmap's own rules of
engagement say to split rather than silently under-deliver. Stage 09's review is the cautionary
example on the other side: a stage taken to "DONE" on an inline self-review turned out to be carrying
eight defects, four of them serious, and the thing that made them expensive was that the stage had
already been called finished.
**Decision.** Stage 10 ships price lists and price resolution, the promotions engine, and
returns/credit notes. **Stage 10c** picks up quotes, invoices and sales analytics.
**Consequences.** The split falls on a dependency line rather than on a convenient volume of work.
Everything in Stage 10 is something another stage is blocked on: Stage 09's till has no price without
the resolver, Stage 10b prices a lay-by through it at the moment it is laid by, and Stage 11's bulk
import needs `PriceListLine`'s uniqueness rule to be idempotent. Nothing in 10c blocks 10b, 11 or 12 —
Stage 14 (order management) is the natural neighbour for sales documents, and Stage 29 owns read
models and KPIs, so analytics built here would be built twice and the second build would have to
reconcile with the first. The cost is one more numbered stage in a plan that has already been renumbered
once, and a shop that wants to quote before it can invoice waits for 10c. A quote is a priced basket
with an expiry, so 10c must snapshot this stage's `PriceResolution` rather than re-resolving — an
expired special silently repricing an accepted quote is the failure mode that split creates.

---

## ADR-075 — Tax is computed and stored per line, and no consumer recomputes it from a total — **LOCKED**
**Context.** The Stage 09 handoff asked for this as its own ADR (item 7) and the `money-and-tax`
review sharpened why. The divergence between the sum of per-line rounded tax and tax computed on a
document total is **not** bounded at a cent: it is bounded per line at just under half a cent and grows
linearly, so a hundred-line basket can diverge by fifty. The sharpest statement of it: the same basket,
same customer, same R99.00 taken, declares R13.00 output VAT if the cashier scans a hundred items
individually and R12.91 if they key quantity 100. Nothing customer-visible was wrong when that was
found, because nothing recomputed. The exposure was that the next author would.
**Decision.** Tax is computed once, at the moment a line is created, from the tenant's configured tax
rules, and stored on the line. **No consumer may recompute a document's tax from its total, and no
consumer may recompute a historical line's tax at all.** A sales return derives its tax from the
original line's *stored* tax, pro-rata to the returned quantity — never by putting the refund through
today's rate.
**Consequences.** Yesterday's receipt is not restated by tomorrow's budget speech, which is the whole
of it: a rate change between a sale and its return would otherwise refund a different amount of VAT
than was declared on the sale, and the shop's output tax would stop reconciling to its own receipts
with no single wrong number anybody could point at. `SalesReturnLine` therefore takes no
`ITaxCalculator` — the dependency is absent by construction rather than by convention, the same way
`IFinancialEvent` has nowhere to put a GL account. The pro-rata shares are **cumulative rather than
per-return**: each amount is the rounded share of everything returned so far less the rounded share of
everything returned before, so partial refunds telescope and their sum is exactly the original line.
Rounding each return independently does not have that property and the failure is a refund of money
the shop never took — half of a R60.00 line whose tax is R7.83 rounds to R30.01, so returning both
halves refunds R60.02. Business rule 5 caps the *quantity* that can come back and would not have caught
it. The cost is that a document total is now a reported figure rather than a derivable one, and any
later report that wants tax by rate has to group the stored lines instead of dividing a total.

---

## ADR-076 — A rollback compensates; it never deletes — **LOCKED**
**Context.** R5 promises rollback, and two of §7's hard rules stand directly in its way. Rule 6 says
stock levels are the projection of an append-only ledger and are never a mutable column. Rule 8 says
nothing is hard-deleted. So the obvious implementation of "undo this import" — delete the rows it
wrote, subtract the stock it added — is unavailable twice over, and a module that reached for it would
be quietly special-casing the two invariants the rest of the system is built on.
**Decision.** A rollback is a **compensating** operation, per row, chosen by what the row did. A row
that **created** an entity soft-deletes it. A row that **updated** an entity restores it from a
**before-image** captured at commit. A row that posted a **movement** is reversed by a new, opposite
ledger entry carrying the same batch reference. Nothing is removed from disk, and the ledger only ever
grows.
**Consequences.** The undo is expressible in a system whose invariants forbid deletion, and the audit
trail of a mistaken import survives the correction — which is the honest record: the import happened,
and then it was taken back. The reversal sits next to the original entry in the ledger, so the balance
returns to where it started by the same arithmetic that moved it, and ADR-068's weighted-average cost
is maintained rather than corrupted by a subtraction nobody costed. The cost is that a rolled-back
import is visible forever in the ledger and in `imports`, which is more rows than a delete would leave;
that is the correct trade, because the alternative is a stock balance that changed twice with only one
explanation on file. The before-image is the fragile part — it is per-handler, stores only the fields
that handler can change, and a row that updated something and carries none is **refused**
(`IMPORTS_BEFORE_IMAGE_MISSING`) rather than skipped, because a rollback reporting success having
changed nothing is the exact failure this ADR exists to prevent.

---

## ADR-077 — The CSV reader is hand-written, not a library — **LOCKED**
**Context.** `CLAUDE.md` §4 locks ClosedXML for Excel and PdfPig for PDF text, and says nothing about
CSV. The default instinct is to add CsvHelper.
**Decision.** `CsvImportSourceReader` is a hand-written RFC 4180 reader: quoted fields, embedded
commas, embedded newlines, doubled quotes, CRLF or LF, a UTF-8 BOM, ragged rows, and a configurable
delimiter detected from the header row when unstated.
**Consequences.** RFC 4180 is small and completely specified, and what this module needs from it is
exactly the specification — headers and cells as strings, with no type inference. A general-purpose
CSV library brings a type-inference layer this pipeline must then switch off, because parsing happens
once at validation against the *target's* declared field type and not against whatever the reader
guessed from the cell. Two places that decide what `1 234,56` means is precisely how a locale bug gets
in, and the reader is the wrong place for that decision because it does not know the field. The cost
is roughly two hundred lines that have to be right, which is why the corner cases are enumerated as
tests rather than trusted: quoted comma, quoted newline, doubled quote, BOM, CRLF, ragged row, empty
trailing line. If a future format demands something beyond the RFC, revisit this rather than bending
the reader.

---

## ADR-078 — The command-classification sweep is derived from module manifests — **LOCKED**
**Context.** Every command carries `[CommandSideEffect]`, which is what the read-only guard (R9,
§7 rule 15) reads to decide whether a lapsed tenant may run it. An architecture test proves no command
is unclassified — but it did so against a **hand-listed** set of assemblies, and `PROGRESS.md` §4.16
recorded the obvious problem: the day somebody adds a module and forgets to add it to that list, the
sweep silently stops covering it and reports green.
**Decision.** The sweep enumerates assemblies from the registered `IModuleManifest` implementations
rather than from a literal list. A module that is wired is a module that is swept.
**Consequences.** The failure mode inverts, which is the whole point: forgetting to register a manifest
now means the module is invisible to entitlement gating and metering as well, which fails loudly and
immediately, rather than meaning one architecture test quietly narrows. Adding a module stops being two
edits where the second is easy to forget and has no symptom. The cost is that the architecture test now
depends on the DI registration being correct, which is a slightly wider blast radius for a test failure
— acceptable, because a module absent from the manifests is broken in more serious ways than an
unclassified command.

---

## ADR-079 — An import writes through the domain, never through the DbContext — **LOCKED**
**Context.** A bulk import is the classic place a codebase grows a second way to write its own records.
The tempting shape is fast: build entities directly, `AddRange`, one `SaveChanges`, skip the factories.
It is also how an imported supplier ends up being a subtly different thing from a typed one — missing an
invariant, a default, a domain event — and the divergence is invisible until something downstream
depends on the invariant that the import did not enforce.
**Decision.** A target handler builds entities with the **same `Create` factories the command handlers
use**, and saves them through the **same repositories**. It does **not** call another command's handler
(`CONVENTIONS.md` §4).
**Consequences.** Every invariant that protects the API protects the import, by construction rather
than by discipline — an imported partner is a Stage 06 `Partner`, and the rule that a partner is a
customer or a supplier and never neither is enforced on an imported row by the same line of code that
enforces it on a typed one. The prohibition on calling another handler is the other half: a handler
that calls a handler puts two transactions and two validation passes inside one command, and the
import's own "one transaction, all or nothing" promise stops being true. The cost is per-row work
instead of set-based work, so a fifty-thousand-row import is bounded by round trips rather than by
bandwidth; `MaxRows` exists to keep that bounded, and if it ever becomes the real constraint the answer
is batching the reads, not bypassing the domain.

---

## ADR-080 — A duplicate row is its own preview verdict, not a kind of valid — **LOCKED**
**Context.** Every target handler computes `ImportSemanticVerdict.WouldSkip` — the row matches
something that already exists and the batch's strategy is `Skip` — and three separate doc comments say
the preview shows it as a skip. It did not. `ImportValidationService` branched only on whether the
verdict carried errors, so a `WouldSkip` row came out `Valid`, and the row was not actually marked
`Skipped` until the commit had already been authorised. Found by the Stage 11 integration tests, which
is the second defect in this stage that existed only because the tests were not written yet.
**Decision.** A row comes out of validation as exactly one of `Valid`, `Invalid` or `Skipped`.
`ImportRowVerdict` carries `WouldSkip`, `ImportBatch.RecordValidation` marks such rows `Skipped`, and
`SkippedRows` is therefore a **preview** figure rather than only a post-commit one. Consequently
`BeginCommit` refuses a batch only when it has neither valid nor skipped rows — which is what
`IMPORTS_NOTHING_TO_COMMIT` has always said in words: *every row on this batch is invalid*.
**Consequences.** The preview stops overstating what a commit will do. A 4,000-row supplier sheet where
3,900 partners already exist previews as 100 valid and 3,900 skipped, instead of 4,000 valid followed
by a commit that changes a hundred things — which is the preview being dishonest that business rule 1
exists to prevent, and it would have been discovered by a shopkeeper rather than by a test. The
loosened `BeginCommit` guard is the necessary other half: with skips counted separately, a file whose
rows all already exist would otherwise be **refused**, and "everything you are importing is already
here" is a successful no-op, not a failure. A batch in that state now commits, applies nothing, and
says so in its counters. The commit-time skip path stays, because it answers a different question — a
row that became a duplicate *between* the preview and the commit, which is a real race and not a
preview error.

---

## ADR-081 — A purchase order posts nothing to the general ledger — **LOCKED**
**Context.** Stage 12 raises a document that says the shop will spend R9,200. It is tempting to post it
— a commitment is a real thing a business wants to see, and "committed spend" is a report every buyer
asks for. Doing it means either a journal against a commitments account or an encumbrance ledger, and
either one makes the trial balance include money nobody has spent.
**Decision.** A purchase order is a **promise, not a transaction**. It posts nothing. Value enters the
books at *receipt*, through Stage 08's ledger and the existing `inventory.receipt.posted` rule, and the
supplier liability crystallises at the *three-way match* (ADR-085). Committed spend is a **query** over
open orders, not a ledger balance.
**Consequences.** The trial balance only ever contains money that has actually moved, which is what a
trial balance is for; a cancelled order needs no reversing journal, because there was nothing to
reverse. An amendment (business rule 3) is likewise free — it cancels one document and opens another,
and neither touches Finance. The cost is that "what have we committed to spend" has to be answered by
reading `procurement.purchase_orders`, which is a report Stage 29 will write and not a number that
falls out of the GL. That is the right trade: an encumbrance ledger is a real accounting technique and
it belongs to a business that has asked for one, not to the module that first noticed it could.

---

## ADR-082 — A three-way match is a document, whatever its verdict — **LOCKED**
**Context.** The obvious shape for a match is a check: compare, and refuse the invoice if it does not
agree. That makes `POST /matches` return a 422 on a variance, and it stores nothing. Three weeks later
somebody asks why a supplier has not been paid, and the only record of the comparison is in the memory
of whoever ran it.
**Decision.** `SupplierInvoiceMatch` is **written whatever the verdict**, including `Blocked`, with the
per-line variances and the tolerances that were applied snapshotted onto it. Running a match is a `201`
even when the answer is "do not pay this". Only *releasing* a blocked match refuses, with a 422, and
the database re-asserts that with `ck_supplier_invoice_matches_blocked_not_released`.
**Consequences.** "Why did we not pay this invoice" is answerable from the data, and so is the more
valuable question behind it: which suppliers are systematically over-billing. `GET /procurement/matches
?status=Blocked` is a real work queue rather than a report somebody has to reconstruct. The snapshotted
tolerances are the other half — tenant policy changes, and a match that re-judged itself under this
month's tolerance would be an audit record that changes its own story. The cost is rows for
comparisons that were rejected, which is exactly the cost of having an audit trail at all.

---

## ADR-083 — Over-receipt is a per-line tolerance, and beyond it the line is refused — **LOCKED**
**Context.** A supplier delivers 105 cases against an order for 100. Three options: accept it silently,
refuse the whole delivery, or accept it up to a bound. Accepting silently means the shop pays for stock
nobody agreed to buy and the three-way match has nothing to object to. Refusing outright means a
loading dock argument over a 5% overage that most trades treat as normal.
**Decision.** A tenant-configured `OverReceiptTolerancePercentage`, applied to the **cumulative**
received quantity per order line, defaulting to **zero**. Within it the delivery is accepted; beyond
it the line is refused with a message that says to amend the order or send the excess back. The
comparison is rounded to `Quantity.Scale` on both sides before it is made.
**Consequences.** A shop that has not thought about this gets the conservative answer rather than a
silent liability, and one that has thought about it configures the number its trade actually uses. The
cumulative check is what makes it real: two receipts of 60 against an order for 100 each pass on their
own, and the pair does not. The rounding is not fussiness — 3 × 1.05 is not exactly 3.15 in decimal
arithmetic, and without it a delivery of exactly the allowed quantity is refused at some quantities and
not others, for a reason nobody on a loading dock could ever discover.

---

## ADR-084 — A supplier scorecard is a snapshot over a closed period, never a live figure — **LOCKED**
**Context.** A supplier rating is naturally a query: count the deliveries, count the late ones, divide.
It is also naturally wrong. A late delivery landing today silently restates last quarter, so the rating
a buyer took into a negotiation is not the rating anybody can reproduce afterwards — and a number that
moves when nobody did anything is one nobody trusts enough to act on.
**Decision.** `SupplierScorecard` is a **frozen row**, taken once per supplier per period
(`ux_supplier_scorecards_partner_period`), and only after the period has **closed** — a period ending
today is still running and is refused. The raw counters are stored alongside the derived rates, because
"80% on time" over five deliveries and over five hundred are different facts.
**Consequences.** Last quarter's rating is last quarter's rating, permanently, and two people reading it
on different days see the same number. A late delivery affects the period it lands in, which is where a
supplier's failure to deliver on the promised date actually belongs. The cost is that the current
period has no scorecard at all, which is correct and will still surprise somebody: what a buyer wants
mid-month is a *live report* over open orders, and that is Stage 29's job, not this row's. The
`OverallRating` is a deliberately plain average of the three service measures — a weighting is
commercial policy, it differs by category, and inventing one inside a domain entity would be exactly the
sort of unilateral cross-cutting decision `AGENTS.md` says a single stage does not get to make.

---

## ADR-085 — Procurement raises a matched fact; the AP invoice document stays Finance's — **LOCKED**
**Context.** A released three-way match means the shop owes a supplier a specific amount. Stage 07
already has `ApInvoice`, its ageing and its payment. The obvious move is for procurement to create one.
That would put a second module in the business of authoring Finance's documents, and the two would
share a table the moment anybody wanted to amend either.
**Decision.** Procurement raises **`procurement.invoice.matched`** through
`IProcurementFinancialEventPublisher`, carrying `Net`, `Tax` and `Gross` and the supplier's own invoice
number as the source reference. The seeded posting rule relieves goods-received-not-invoiced, debits
input VAT and credits trade creditors. The `ApInvoice` aggregate, its ageing and its payment remain
Stage 07's, and the two modules share no table.
**Consequences.** §7 rule 12 holds by construction — there is nowhere on the event to put an account —
and Finance keeps sole authorship of its own documents. The publisher returns the journal id, unlike
Stage 10's, because accounts payable reconciling a supplier dispute needs to get from the match to its
journal without a search. The gap this leaves is honest and worth naming: a released match is a
liability in the *ledger* and is not yet an AP *invoice*, so it does not appear in the AP ageing until
somebody raises one. Closing that is a Stage 07 or Stage 29 job — a rule that turns a matched fact into
an AP document — and doing it here would be procurement deciding how Finance ages its creditors.

---

## ADR-086 — Stock is valued at the net cost, never at a tax-inclusive one — **LOCKED**
**Context.** Found while verifying Stage 12's seed against the ledger, not by a test. `CLAUDE.md` §9
makes VAT-inclusive pricing the South African default, so a purchase order line's `UnitCost` is
typically the **gross** figure a supplier quotes. The goods receipt valued stock at exactly that, which
is wrong twice over: it capitalises recoverable input VAT into inventory — overstating the balance
sheet by the VAT rate and cost of sales when the goods sell — and it strands the VAT in the clearing
account forever, because the receipt credits goods-received-not-invoiced **gross** while the three-way
match debits it **net**. On the demo tenant that was R279.91 left behind on a single R2,220 order, on
every purchase, with nothing that would ever clear it.
**Decision.** `PurchaseOrderLine.NetUnitCost` is the line's stored `Net` divided by its quantity, and
that is what `GoodsReceiptLine.UnitCost` snapshots and what the stock ledger is posted at. It is derived
from the **stored** net rather than recomputed through the tax engine, because ADR-075 says nothing
downstream recomputes an order's tax. The three-way match still compares the supplier's invoiced unit
cost against the *as-ordered* one — that comparison is like for like and unaffected.
**Consequences.** Inventory is carried at cost, the clearing account clears, and a tenant who is not VAT
registered has a rule that computes zero tax, so net equals gross and nothing changes for them. One
residual is left and is named rather than hidden: `Net / Quantity` rounds to `Money.Scale`, so a receipt
of 116 against an order for 120 clears the clearing account to within **under a cent** rather than
exactly. That is an ordinary purchase price variance, it is bounded by the rounding scale rather than by
the transaction size, and the right home for it is a purchase-price-variance account in a later Finance
stage — not a fudge in this one. The broader lesson is recorded in `PROGRESS.md`: this defect built
clean, passed every test, and was found only by reading the seeded journal.

---

## ADR-087 — A bin id is an additive column on `StockLedgerEntry`, not a parallel ledger — **LOCKED**
**Context.** Stage 08's own notes said this would happen: "Stage 13 subdivides a location into bins. The
ledger's `LocationId` stays; a `BinId` beside it is additive." Stage 13 needs to know *which bin* some
movements happened at (a shipment picked from, a cycle count counted) without inventing a second source
of truth for quantity at location granularity. The alternative — a bin-scoped ledger that mirrors
`StockLedgerEntry`'s shape and requires every reader to reconcile two tables — would let the two
disagree about what a location holds, which is exactly the class of bug ADR-005 exists to prevent.
**Decision.** `StockLedgerEntry` and `StockBalance` gain a nullable `BinId`, threaded through
`IStockLedgerPoster` as an optional parameter (default `null`) on every method a bin-aware caller might
use. Every existing call site — Stage 08's own commands, Stage 09's sale issue, Stage 10's return
receipt, Stage 12's purchase receipt — compiles and behaves identically unchanged, because none of them
pass it. Two new methods, `IssueForShipmentAsync` and `PostCycleCountVarianceAsync`, follow ADR-085's
precedent: a new caller of an existing movement type gets its own method and its own
`StockReferenceType` (`Shipment`, `CycleCount`) rather than overloading somebody else's.
**Consequences.** One ledger, one balance, one source of truth for "what does this location hold" —
Stage 13 only adds "and where inside it". A movement that does not change the location's total (a
putaway, an internal bin move) never calls `IStockLedgerPoster` at all; it posts only to the new
bin-level `BinStockMovement`/`BinStock` pair, which is where the actual net-new ledger this stage builds
lives. The regression this decision creates a specific risk for — a future edit to `StockLedgerPoster`
that forgets `BinId` is optional and breaks an old call site — is covered by a unit test asserting the
column is `null` when nobody supplies it.

---

## ADR-088 — Bin-to-bin moves get their own append-only ledger, one level below the stock ledger — **LOCKED**
**Context.** Putaway, and an internal bin-to-bin transfer, redistribute stock inside a location without
changing what the location holds (business rule 2). They cannot go through `IStockLedgerPoster` — every
path there changes `StockBalance` — so they need a bookkeeping mechanism of their own, and R6's audit
trail requirement ("who moved what, when") applies to a bin reassignment exactly as it does to anything
else that changes a number.
**Decision.** `BinStockMovement`, an `IImmutableRecord` at bin granularity: signed quantity, a
`BinStockMovementType` (PutawayIn/PutawayOut/PickReserve/PickRelease/InternalTransferIn/
InternalTransferOut), and a `BinStockReferenceType` + `ReferenceId` correlating it to the task or
transfer that caused it — the same shape `StockLedgerEntry` uses for its own correlation. `BinStock` is
the projection it sums to, maintained transactionally alongside every insert, exactly as `StockBalance`
is Stage 08's projection of `StockLedgerEntry`.
**Consequences.** A picker who scans the wrong bin, a putaway split across two shelves, an ad-hoc
bin-to-bin tidy-up — all of it is reconstructable after the fact from an append-only record, the same
guarantee `CLAUDE.md` §7 rule 6 gives the location-level ledger, one level down. `IBinStockMover` is the
one place a handler posts one, the same shape `IStockLedgerPoster` gives Stage 08's six posting paths
(`CONVENTIONS.md` §4).

---

## ADR-089 — A cycle count variance posts through `IStockLedgerPoster`, not only into `BinStock` — **LOCKED**
**Context.** A bin-level physical count that disagrees with the system is telling the shop it has more
or less stock than it thought — the same fact a full Stage 08 stocktake discovers, at finer granularity.
Posting the variance only into `BinStock` would make a cycle count a bin reshuffle rather than what it
actually is, and would leave `inventory.stock_balances` silently wrong until somebody ran a full
stocktake to notice.
**Decision.** `FinalizeCycleCountCommand` posts every non-zero line variance through
`IStockLedgerPoster.PostCycleCountVarianceAsync` — reusing `StockMovementType.StocktakeVariance` (the
economics are identical to a Stage 08 stocktake's variance) under its own `StockReferenceType.CycleCount`
(so the two counting workflows are never confused in an investigation, ADR-085's precedent again) — and
applies the same signed delta to the bin's own `BinStock` row.
**Consequences.** A cycle count variance reaches the general ledger through the exact posting rules
Stage 08 already seeded (`inventory.stocktake.shortage` / `.surplus`); Stage 13 registers no posting rule
of its own, which the exit checklist verifies rather than assumes. The location's `StockBalance` and the
bin's `BinStock` can never drift apart after a finalize, because both are written from the one variance
figure in the one command.

---

## ADR-090 — Pick allocation is largest-bin-first, behind a strategy interface — **LOCKED**
**Context.** Something has to decide which bin(s) satisfy a pick line's demand when more than one bin
holds the stock-keeping unit. A real slotting/wave-optimisation engine (pick-path routing, zone
sequencing, FEFO/FIFO by lot) is a project in its own right and not this stage's to build — but shipping
with no allocation logic at all is not an option either.
**Decision.** `IPickAllocationStrategy.Allocate(requested, candidates)` returns one or more
`BinAllocation`s. The one implementation, `LargestBinFirstAllocationStrategy`, tries the bin holding the
most of the stock-keeping unit first and spills into the next largest only if the first cannot fully
satisfy the demand. `ReleasePickWaveCommandHandler` creates one additional `PickTask` per extra bin the
allocator draws on, rather than teaching `PickTask` to hold more than one bin.
**Consequences.** A working, testable allocator ships today, and it is explicitly not claimed to be
optimal — the stage document says so in words, and the interface seam is what lets Stage 15 or 24 replace
it later without any caller changing. Minimising bin visits per line is the one easy win available with
no routing data behind it; a picker touches the fewest possible shelves for one line's demand.

---

## ADR-091 — Bin capacity is recorded, not enforced — **LOCKED**
**Context.** `Bin.Capacity` exists for reporting and a future slotting stage. Refusing a putaway that
would exceed a capacity nobody has actually measured — no bin dimensioning, no item cube, no weighing —
would block a legitimate putaway on a number that is frequently just wrong, which is worse for a shop
than not enforcing one at all.
**Decision.** Capacity is stored (`Bin.CapacityValue` / `CapacityUnitOfMeasure`, both nullable) and
never checked by any command in this stage. `ConfirmPutawayCommand` and `MoveBinStockCommand` succeed
regardless of what a bin's recorded capacity says.
**Consequences.** A dense warehouse can genuinely overfill a bin with no warning from this stage — a real
gap, named here rather than silently accepted. Whichever later stage adds bin dimensioning and weighing
is the one that earns the right to enforce it; until then, a number nobody measured enforcing itself
would be a worse failure mode than no enforcement.

---

## ADR-092 — Order allocation has no reservation ledger of its own; it is read live from `PickTask` — **LOCKED**
**Context.** A `SalesOrder` needs to know how much of a line is promised and how much has shipped.
Stage 13's `PickTask.AllocatedQuantity` and `PickedQuantity`, keyed by `OutboundReference`, already mean
exactly that — `PROGRESS.md`'s own note when Stage 13 shipped named this the reason `OutboundReference`
is free text rather than a typed foreign key. Persisting a second `AllocatedQuantity`/`FulfilledQuantity`
column on `SalesOrderLine` would create two numbers that are supposed to always agree and occasionally
would not — a stale order-side cache the moment a pick is short, a task is cancelled, or a wave ships
outside the order's own transaction.
**Decision.** `SalesOrderLine` persists only `BackorderedQuantity` — the one figure Stage 13 cannot ever
report, because it is demand this stage never even attempted to allocate. Everything else is computed on
demand by `IOrderFulfilmentReader`, summing `PickTask` state across every task carrying a line's id as
`OutboundReference`: open (not shipped, not cancelled) tasks contribute to "allocated", tasks whose
parent `PickWave` has shipped contribute to "fulfilled". `ConfirmOrderCommand` and
`ReattemptBackorderedAllocationsCommand` go further than reading Stage 13's answer: the bin allocator's
actual result, not `IOrderFulfilmentReader.GetAvailableToPromiseAsync`'s location-level estimate, decides
what counts as allocated — a location can show stock on hand that has never been put away into any bin,
and trusting the estimate over the allocator would silently promise stock nobody can actually pick.
**Consequences.** `LineStatus` is always a true reflection of Stage 13's current state, never a value
that needs reconciling against it — there is nothing to reconcile. The cost is an extra read per line on
every query and every allocation attempt, which is the trade the stage document names explicitly: a
second reservation concept next to `PickTask` was rejected because two numbers that must always agree
and occasionally will not is exactly the class of bug this stage exists to avoid, not one it exists to
add. **Superseded in direction, not yet in code, by ADR-102/ADR-103**: the amended Stage 14 spec sources
allocation from Stage 08c's cross-company reservation service instead of a direct `PickTask` read. The
code merged here still implements the `PickTask`-read design; migrating it to 08c is open follow-up work
(see `docs/PROGRESS.md`).

---

## ADR-093 — An order return receives stock back at the traced shipment's unit cost, under its own reference type sharing the sales-return movement — **LOCKED**
**Context.** `CompleteOrderReturnCommand` puts stock back for goods that were fulfilled off a
`SalesOrder`, never rung up as a Stage 09 `Sale`. Economically the movement is identical to a Stage 10
sales return — stock coming back, valued at what it left at — but it has a different document behind it,
and business rule 6 requires the two document families (`sales.sales_returns` reversing a till `Sale`,
`orders.sales_order_returns` reversing a `SalesOrder` line) never be confused in an audit trail.
**Decision.** `StockReferenceType.OrderReturn`, a new value sharing `StockMovementType.SalesReturn` —
ADR-085's precedent (a new caller of an existing concept gets its own reference/event type, not a
duplicate) applied here exactly as ADR-088/089 already applied it inside Stage 13. The unit cost is not
read off the order line (which stores what the customer was charged, the wrong number for a stock
receipt) but traced back through `IOrderFulfilmentReader.GetShipmentUnitCostAsync`, which finds the
`ShipmentConfirmation` that actually fulfilled the line and reads the quantity-weighted average cost off
the `StockLedgerEntry` that shipment produced.
**Consequences.** The return reaches the general ledger through the *existing* `inventory.sale.returned`
posting rule with no new rule needed on the stock-value side — reusing the movement type is what makes
that true, and it is verified rather than assumed (the integration suite checks a real journal posts).
When no shipment can be traced for a line — nothing ever actually shipped against it — the return still
completes (`OrderStockReturnStatus.Refused`, ADR-070/073's precedent): inventing a cost basis for stock
that, as far as the ledger is concerned, never left would be a worse defect than a refund with a
reconciliation note attached to it.

---

## ADR-094 — Order revenue and order returns raise their own financial event types, not a reuse of Stage 09/10's — **LOCKED**
**Context.** `CompleteOrderCommand` recognises revenue once per order (business rule 5); a completed
`SalesOrderReturn` refunds a customer. Both look, at the amounts level, like `pos.sale.tendered` and
`sales.return.completed` — same three named amounts, same shape of event.
**Decision.** Two new event types: `orders.order.fulfilled` and `orders.return.completed`, each with its
own posting rule seeded alongside the module. Neither reuses an existing rule.
**Consequences.** This is the one place this stage's own posting-rule story differs from Stage 13's
cycle-count variance (which reused Stage 08's stocktake rules): a till sale and an order's fulfilment are
genuinely different business events with different triggers, different timing (a sale completes when
tendered; an order's revenue is recognised only once every active line has shipped or been cancelled,
which can be days after the first line moved) and — the sharper reason — a tenant may legitimately want
them posted to different accounts (a till sale often settles in cash there and then; an order's revenue
lands against a trade debtor until Stage 09/10b's settlement mechanism clears it). Collapsing the two
into one event type would take that choice away from the tenant. The exit checklist verifies both rules
actually reach a balanced journal against a seeded order rather than assuming the shape is right because
it compiles.

---

## ADR-095 — `SalesOrderReturn` draws from its own `ORT` document series, never Stage 10's `RTN` — **LOCKED**
**Context.** Business rule 6 draws a hard line between `sales.sales_returns` (reverses a Stage 09 till
`Sale`) and `orders.sales_order_returns` (reverses a fulfilled `SalesOrder` line that was never rung up as
a `Sale` at all) — the two document families must never be confused in an audit trail, and neither stage
reads the other's table.
**Decision.** `CreateOrderReturnCommandHandler.ReturnNumberSeries = "ORT"`, ADR-065's gap-free document
number sequence under a series Stage 10's `RTN` never uses.
**Consequences.** A credit document numbered `ORT-000042` is unambiguous at a glance, in a printed
report, and in a search — nobody reconciling a shrinkage investigation has to open the row to find out
which kind of return it is. This is ADR-085's precedent (a new caller of an existing concept gets its own
identifier) applied to a document series instead of a stock movement or financial event type — the third
time this stage applies the same shape of decision (ADR-093, ADR-094, and this one), because it is the
same discipline every time: two things that are economically similar but institutionally distinct get
distinguishable identifiers, not a shared one a query has to disambiguate after the fact.

---

## ADR-096 — Revenue is recognised once per order, at completion, not per partial shipment — **LOCKED**
**Context.** An order can ship in more than one wave — the stage document's own seeded example
deliberately backorders a line so the demo exercises exactly this. Two shipment events could each
recognise their own share of revenue as they happen, or the order could recognise all of it once,
in one place, when it is done.
**Decision.** `SalesOrder.RecogniseRevenueIfDue` fires once, guarded by `IsRevenueRecognised`, when every
active line has either shipped in full or been cancelled — never per shipment. A second
`CompleteOrderCommand` call after the first is a no-op, asserted by a unit test and an integration test
both.
**Consequences.** This is a stated v1 simplification, named as one rather than discovered as a gap later.
It is materially simpler to reason about and to test — one journal per order, one place `IsRevenueRecognised`
can ever flip — at the cost of an order that ships 90% today and the remainder next month recognising
nothing until the remainder ships, which does not match strict accrual accounting for a long-tailed
order. A future stage that needs per-shipment recognition changes rule 5 deliberately, with its own ADR
explaining the trade-off the other way; it is not an oversight this ADR is quietly correcting in advance.

---

> **ADR-097 and ADR-098 remain unused.** They were reserved alongside ADR-092–096 for Stage 14 but the
> stage did not need them; the numbering continues at ADR-099 below rather than backfilling the gap.

---

## ADR-099 — Every company is its own database; a per-tenant registry database holds what spans them — **PROPOSED, supersedes the logical-company form of this ADR filed earlier the same day**
**Context.** A tenant runs several trading companies — a hardware business, a distribution centre, a
grocery business — and the operator's requirement is physical separation: *each company will have their
own database*. The first form of this ADR proposed logical separation inside one database on
engineering grounds (no distributed transactions, cheaper group queries). **The operator overruled it,
and this is the decision that stands.** Physical separation is also what a customer buying "three
companies" reasonably expects: one company's data can be backed up, restored, exported, audited, sold
or shut down without touching another's.
**Decision.** Per tenant: **one database per company**, plus **one registry database** holding what
genuinely spans them — the company list and their connection details, company groups, credit groups and
their exposure ledger, the catalogue routing index, group receipts and payments, and the group-level
read models. Company databases all carry the same schema and the same migration chain; the registry has
its own. Nothing in a company database references a row in another company database, and **there is no
cross-database transaction anywhere in this product** (ADR-116). Reads that span companies go through
registry read models or a bounded fan-out; writes that span companies are sagas.
**Consequences.** Isolation is a wall rather than a filter, and per-company backup, restore, export and
retention become trivial — a real gain for R4 and for POPIA. The price is paid in five places, each of
which gets its own ADR because each is a place this design can go wrong: cross-company writes are
eventually consistent (ADR-116), group reads are stale by design and every figure carries `AsAt`
(ADR-119), migrations must fan out and can be partially applied (ADR-117), a company's lifecycle now
includes provisioning a database (ADR-118), and correctness under stale group data depends on the
authoritative check always re-running in the owning company database (ADR-102). `postgres_fdw` was
considered and rejected: it would make the registry unable to answer while any one company database is
down, which is the opposite of what physical separation is for.

## ADR-100 — A scan resolves through the registry's routing index, and never guesses a company — **PROPOSED**
**Context.** "You scan and it will pick up automatically which store it is." With one database per
company, the till cannot query every company's catalogue on each scan — that is N round trips against N
databases, any of which may be offline, on the hot path of every sale.
**Decision.** The registry holds `registry.catalog_routing_index`: `(tenant_id, barcode)` → company id,
item id, variant id, pack size, and a cached description. It is a **projection**, fed by each company
database's outbox whenever a barcode is created, changed or retired, and rebuildable by asking every
company database to republish. A scan is one probe against the registry, then the item detail is read
from the owning company's database. Collisions resolve by the terminal session's company, then the
company with available stock at the scanning location, then a `MultipleCompanyMatches` result carrying
every candidate — it never picks one silently. In-house barcodes are namespaced per company on issue.
**Consequences.** Scan latency is one indexed probe plus one company read, independent of how many
companies exist. The index is eventually consistent, so a barcode created seconds ago in another company
may not resolve yet; the fallback is the scanning company's own catalogue, and the operator is told the
lookup was local rather than group-wide. If the registry is unreachable the till degrades to its own
company and keeps trading (R1 outranks group convenience), and the receipt still prints — it simply
cannot sell a sister company's stock until the registry is back.

## ADR-101 — The group credit limit lives in the registry and is consumed by a hold token, not a shared transaction — **PROPOSED**
**Context.** One customer must have R150 000 across three companies, and the three companies are now
three databases. The invoice is written in a company database; the limit lives in the registry. They
cannot commit together, and a check in one followed by a write in the other is precisely the race that
lets two companies both spend the last of a limit.
**Decision.** `registry.credit_groups` holds the limit and an append-only exposure ledger.
Consuming credit is a **serialisable transaction in the registry alone**, which issues a **hold token**:
an amount, a company, a document reference and an expiry. The company database then writes its document
carrying that token, and its outbox confirms the consumption back to the registry idempotently. If the
document is never written — the session died, the till lost power — the hold **expires by itself** and
the credit returns. Exposure is therefore the sum of confirmed consumption plus unexpired holds, and it
is never inflated by a document that does not exist.
**Consequences.** Two tills in two companies cannot both spend the last R5 000: whichever transaction
serialises second gets a refusal, not a stale yes. A crash between hold and document costs the customer
that credit only until the hold expires, which is the correct failure direction — briefly too strict
rather than briefly overdrawn. A held-but-unconfirmed report is an operational necessity, not a nicety,
and there is a concurrency test against a real registry database, not a mock. If the registry is
unreachable, credit sales are refused for that customer while cash sales continue — a store that cannot
reach the ledger of record must not grant credit against it.

## ADR-102 — The plan is built from group data; the reservation is always taken in the owning company's database — **PROPOSED**
**Context.** "Whichever stock is available on whatever database, without going into a negative." Group
availability is now a read model in the registry, and it is stale by construction. Sourcing an order
from a stale number is exactly how two orders both take the last unit.
**Decision.** Sourcing is two steps and the second one is authoritative. `ICompanySourcingStrategy`
builds a **plan** from the registry's availability read model — a projection, explicitly labelled with
its `AsAt`. Committing that plan opens a **local, serialisable transaction in each named company's own
database**, which re-reads that company's real availability and reserves what is actually there. A leg
that cannot fully satisfy its share reserves what it can and reports short; the saga re-sources the
shortfall against the remaining companies once, and whatever is still uncovered becomes a backorder line.
**Consequences.** Stale group data can cause a re-plan; it can never cause a negative, because no
company's stock is ever committed by anything other than a transaction inside that company's own
database. That is the single correctness argument this whole design rests on, and it is tested by
deliberately serving the planner a stale read model and asserting the outcome is a backorder, not an
oversell. The cost is that an order can take two rounds to source and that a customer may be told
"we had it a second ago" — which is true, and better than shipping a promise nobody can fill.

## ADR-103 — A reservation reduces *available* and is local to its company's database — **PROPOSED**
**Context.** The operator asks that an approved order "set the stock for that order and remove it from
stock". Deducting on-hand would either mutate a quantity column — forbidden by `CLAUDE.md` §7 rule 6 and
settled by ADR-005 — or write a stock issue for goods that have not moved, recognising cost of sale for
a sale that has not happened.
**Decision.** `inventory.stock_reservations` is an append-only ledger **inside each company's database**.
A reservation reduces that company's `Available` (`OnHand − Reserved − InStaging`) immediately and leaves
`OnHand` alone until goods physically ship. Release, consumption and expiry are new rows, never edits.
No reservation ever spans companies: an order needing two companies' stock holds two reservations, one
in each database, tied together only by the group document reference.
**Consequences.** The operator gets what they want — nobody else can sell that stock the moment the
order is approved — without a false ledger movement, and each company's stock position stands alone and
restores alone. Two numbers now exist where users expect one, so every API and screen shows *available*,
labelled, with its `AsAt`; showing on-hand where a user reads "can I sell it" is a reportable defect.

## ADR-104 — A group receipt lives in the registry, posts nothing, and dispatches allocations to company databases — **PROPOSED**
**Context.** A customer pays R9 000 once against accounts in three companies and the operator wants to
capture the total and split it — R1 000, R3 000, R5 000. Those are three databases; one financial
document cannot span them, and one transaction cannot write them.
**Decision.** `registry.group_receipts` records the tender, the reference and the receiving bank account,
and **posts nothing**. Each allocation is dispatched to its company's database through the registry's
outbox as an idempotent command keyed by `(group_receipt_id, allocation_id)`, where it creates a real
`finance.ar_receipts` row and posts through that company's own posting rules. An allocation is `Pending`
until its company acknowledges, then `Applied`; a leg that has not been acknowledged inside its timeout
raises an operational alarm and appears on the unapplied-legs report. An unallocated remainder sits on
the registry container, in nobody's ledger, visible and ageing. Group supplier payments use the same shape.
**Consequences.** Every company's books balance on their own, and the group screen still shows the one
payment the customer made. Between capture and acknowledgement the money is visibly in flight, which is
honest — and the unapplied-legs report is the control that stops "in flight" from quietly meaning "lost".
Replaying a leg is safe by construction, so recovery after a company database outage is to retry, never
to re-key.

## ADR-105 — Inter-company clearing is a paired saga with a standing reconciliation, not a two-legged transaction — **PROPOSED**
**Context.** Cash from a group receipt lands in one company's bank account and settles a sister company's
invoice. Both companies must post, and they are two databases: there is no transaction that can hold both.
**Decision.** The registry raises one `InterCompanyClearingIntent` — immutable, carrying both legs — and
dispatches each leg to its own company database, idempotently. The receiving company debits bank and
credits inter-company clearing; the benefiting company debits clearing and credits its customer. Each leg
posts locally through the posting-rules engine. The intent is `Settled` only when both legs acknowledge.
**Net-zero is enforced by a standing reconciliation across the databases, run on a schedule and after
every intent**, not by a transactional assertion — because there is no longer one to make. An intent with
one leg outstanding past its timeout is an alarm with a named owner, and clearing accounts carry the
intent id so an unmatched leg is identifiable by document, not by guesswork.
**Consequences.** This is the sharpest edge in the multi-company design and it is named as such: a
half-applied intent is a real state that can exist for seconds and, if a database is down, for longer.
The mitigations are that legs are idempotent (retry is always safe), that the outstanding set is small
and visible, and that a reversal reverses the intent rather than a leg. What is explicitly **not**
acceptable is a period being closed with an intent outstanding — Stage 07c's close refuses it and names it.

## ADR-106 — Consolidated reporting reads every company and is a labelled read model that cannot be posted to — **PROPOSED**
**Context.** A group owner wants one view across three companies; the tax authority wants three sets of
statements. With three databases, "one view" is now an aggregation across three sources rather than a
`GROUP BY`.
**Decision.** Every statutory statement — trial balance, income statement, balance sheet, VAT return, age
analysis, cash book — is produced **inside one company's database**, always. Consolidation is a separate
read model assembled in the registry from each company's published period figures, eliminating
inter-company clearing, carrying `IsConsolidated`, the `AsAt` of each contributing company, and the
watermark *Consolidated — management information, not a statutory statement.* A consolidated report whose
contributing companies are not all current says which are stale rather than quietly summing old figures.
Nothing posts to it. Period close is per company; a group close is every member closed, and it refuses to
complete with an inter-company intent outstanding (ADR-105).
**Consequences.** No consolidated number can be mistaken for a filing, and no VAT return is ever computed
across companies. The staleness disclosure is what makes the consolidated view trustworthy: a group owner
reading it during a company outage sees which third of it is old, instead of being quietly misled.

## ADR-107 — A pro forma posts nothing and reserves nothing; approval creates the document — **PROPOSED**
**Context.** Reps on the road capture orders and credit notes against customers, offline, at prices and
quantities nobody has yet checked. If those documents committed anything, an unreviewed rep would be able
to move stock, grant credit and recognise revenue from a phone.
**Decision.** `ProFormaOrder` and `ProFormaCreditNote` are proposals: no GL posting, no AR, no stock
ledger, no reservation, their own `PF-`/`PFC-` sequences per company, and an expiry (7 days by default).
They carry a price-list and promotion snapshot plus the availability `AsAt` shown to the rep. Approval —
through Stage 05's `IApprovalService`, never a local implementation — converts a pro forma into a Stage 14
sales order or a Stage 10 credit note. An expired pro forma must be re-priced before it can be approved.
**Consequences.** Availability quoted to a customer is explicitly indicative, and the rep's client must say
so; the alternative — reserving on capture — would let one rep's speculative order paralyse a warehouse.
The guard agent proves the absence of the poster and reserver dependencies rather than reading the happy
path, because "it doesn't post today" is a property that regresses quietly.

## ADR-108 — Approval is a saga: credit hold, then per-company reservations, then the order — and it compensates — **PROPOSED**
**Context.** Approving a rep's pro forma re-prices, sources across companies, reserves stock and consumes
group credit. Under ADR-099 those touch the registry and one or more company databases, so the earlier
"all in one transaction" form of this ADR is not implementable. What must survive is its guarantee: an
approval never leaves stock held against nothing, or credit consumed for an order that does not exist.
**Decision.** Approval runs in a fixed order, each step locally transactional, each step compensatable,
and the whole thing driven by an immutable `ApprovalIntent` in the registry so a crashed approval resumes
rather than restarts:
1. **Re-price** in the ordering company's database, and show the approver the delta against what the rep
   quoted. No side effects.
2. **Take the credit hold** in the registry (ADR-101) — the step that expires by itself if everything
   after it fails.
3. **Reserve** in each sourcing company's database, one local serialisable transaction each (ADR-102,
   ADR-103). A short leg re-sources once, then backorders.
4. **Create the order** in the ordering company's database, carrying the hold token and the reservation
   references.
5. **Confirm** the hold to the registry; the intent completes.
A failure at any step compensates every completed step in reverse — reservations are released by a new
ledger row, the hold is released or left to expire — and the pro forma returns to `Submitted` with the
reason. There is no partially approved state.
**Consequences.** The guarantee is preserved without a distributed transaction, and the ordering is
deliberate: credit is held before stock is reserved, so the expensive, contended resource is checked
first and released automatically if anything downstream dies. The window in which stock is reserved and
the order does not yet exist is one step wide, bounded by the reservation's own expiry, and visible on
the in-flight approvals report. Approval is now resumable, which matters more than it sounds: a
back-office session that dies mid-approval is a Saturday-morning support call otherwise.

## ADR-109 — A rep sees *available*, stamped, and never on-hand or cost by default — **PROPOSED**
**Context.** "All orders by reps, they should be able to see how much stock is available." A rep with a
stale, confident number promises what does not exist; a rep with the on-hand number promises what is
already sold.
**Decision.** The rep API returns `Available` per company, group-wide within the rep's permitted
companies, always with `AsAt`. Cost, margin and other territories' customers are unreachable without the
permission that grants them — structurally, not by a filter a caller may forget. A device that has been
offline shows the timestamp of what it holds rather than presenting it as current.
**Consequences.** The rep's app shows an honest number that is sometimes old, instead of a fresh-looking
number that is sometimes wrong. Territory enforcement is server-side, so a guessed customer id returns
403 rather than a filtered empty result that would confirm the customer exists.

## ADR-110 — Rep performance is a closed-period snapshot with an explicit comparison period — **PROPOSED**
**Context.** "Check how much reps do per month and compare that over a time period." A live-computed
figure changes under a manager who printed it yesterday, and a rep paid or judged on a number that moves
has a legitimate grievance.
**Decision.** `RepPerformanceSnapshot` is computed once for a **closed** period and stored — the same shape
as the supplier scorecard (ADR-084) — per rep, per company, with the group roll-up. Metrics: pro formas
captured, conversion (approved/rejected/expired), invoiced, credited, net, margin where permitted,
collections, and customer coverage. Every query takes a comparison period and returns both sets plus the
variance, so the desktop and the Android app cannot disagree. Targets are versioned; changing one never
restates a measured period. A recomputation creates a new version with a reason, never a silent overwrite.
**Consequences.** Month-on-month and year-on-year comparison is a first-class API rather than a
spreadsheet job. The current, open period is explicitly a running estimate and labelled as one. Commission
is deliberately not built here — it belongs with payroll in Stages 25/26; what this owes them is a clean,
auditable net figure.

## ADR-111 — COD is a settlement term on the order, enforced at dispatch, and consumes no credit — **PROPOSED**
**Context.** An order can be cash on delivery, and that changes two things: the customer's credit is not
at stake, and the goods must not leave without the money being accounted for.
**Decision.** `SettlementTerms` on the order carries `CashOnDelivery`, inherited by every invoice the order
produces and printed on the delivery note and the invoice. A COD order **consumes no credit-group
exposure**. Dispatch is gated: a COD order cannot be released for dispatch without either a captured
payment or an explicit driver-collect authorisation naming who is collecting on delivery, and the
outstanding COD amount is reconciled against the delivery run when the driver returns.
**Consequences.** COD becomes a genuine control rather than a note in a comments field — the two ways it
fails in practice are goods leaving with no money expected and a driver's collections never reconciling,
and both are closed here. A tenant that does not want the dispatch gate can configure driver-collect as
the default, which records the decision rather than removing it.

## ADR-112 — Pack size is resolved at capture and snapshotted onto the document line — **PROPOSED**
**Context.** "On the invoice we need pack sizes." An item sells as a single, a six-pack and a case, and the
pack size is a property of the barcode/unit-of-measure the operator actually scanned. Re-deriving it when
the invoice prints would print today's packaging on last month's document.
**Decision.** The resolved pack size — quantity per pack, its unit of measure, and the packs-vs-units
breakdown — is snapshotted onto the order, pro forma and invoice line at capture, exactly as price and tax
already are (ADR-075's rule). The invoice prints packs and units both; picking waves group by pack size,
because two pack sizes of one item are two different things to pick (ADR-113).
**Consequences.** An invoice reprinted a year later shows what was actually sold. Changing an item's
packaging cannot restate history. The line carries one more snapshotted field, which is the same trade
already made for price, tax and cost.

## ADR-113 — A consolidated pick wave groups by SKU over a geography snapshot and a period — **PROPOSED**
**Context.** "Everything for that town pulled out one time — ten customers from Durban, five need two hot
plates each, three need five gloves each." Picking each order separately walks the same aisle ten times.
**Decision.** `BuildConsolidatedWaveCommand` takes a period, a geography level (`Province | City |
Suburb`) and a value, and groups every qualifying open order line by **(item, variant, uom, pack size)**
into one pick list, carrying the per-order breakdown alongside each grouped line so consolidation splits
it back without a second query. The geography is normalised at order capture and **snapshotted onto the
order**, so editing an address later cannot silently change what a built wave contains. An order line may
be in exactly one open wave. A wave may span companies; the pick consolidates, the documents do not.
**Consequences.** One walk of the aisle for ten orders, and a wave that is reproducible from its own
snapshot rather than from live customer data. The per-line breakdown is what makes consolidation possible
at all — without it, a picker returns with 15 gloves and no idea whose they are.

## ADR-114 — Staging areas are bins; location state is derived from where the quantity sits — **PROPOSED**
**Context.** "Stock take should notify checkers if items are currently in the consolidating area, packing
area or dispatch area — when the picker picks the item, status moves from shelved to consolidation." A
status flag on an item would be one more thing to remember to set, and would immediately disagree with
the bin ledger Stage 13 already keeps.
**Decision.** `Consolidation`, `Packing` and `Dispatch` are **bin types**. A pick moves stock from a shelf
bin into the wave's consolidation bin through Stage 13's existing `bin_stock_movements` ledger; packing and
dispatch are further moves; shipment confirmation posts the stock ledger issue as it already does.
`StockLocationState` (`Shelved | Consolidation | Packing | Dispatch | Shipped`) is therefore **derived**
from where the quantity is, never stored as a flag. Stock in a staging bin is on hand and **not** available.
**Consequences.** No new machinery, no parallel state to drift, and a movement history that explains
itself. Every existing bin report gains staging visibility for free. Stage 13's handheld flows must write
the staging move at pick confirmation rather than at shipment, which is the one behavioural change 13b
makes to already-shipped code.

## ADR-115 — An interval count targets slow movers and warns when stock is in flight, rather than blocking — **PROPOSED**
**Context.** "Stock takes can be set at intervals where random items which are not being pulled are counted
and checked." The stock most likely to be wrong is the stock nobody has touched, and it is exactly the
stock a movement-triggered count never reaches.
**Decision.** `CountSchedule` generates Stage 13 `CycleCount`s on a cadence, selecting by **last-movement
date** — least-touched first — plus a random sample so the selection cannot be gamed. Where a selected
SKU has quantity sitting in a consolidation, packing or dispatch bin, the count sheet shows that quantity
and its wave/shipment reference, and variance is computed against on-hand **including** staging. The
checker is told, not blocked. A line whose shelf bin is being drawn by an open wave is deferred to the next
run with the reason recorded.
**Consequences.** Slow-moving error is found by design rather than by luck, and the classic false variance
— counting a shelf while six units sit on the packing bench — stops being a variance at all. Deferring
actively picked lines means a schedule can under-deliver on a busy day; that is preferable to a count that
fights the pickers, and the deferral is visible.

## ADR-116 — Cross-database work is a saga with idempotent legs and compensations; there is no two-phase commit — **PROPOSED**
**Context.** ADR-099 makes each company a separate database. Every operation the operator asked for that
spans companies — split invoicing, group receipting, inter-company clearing, approval — therefore spans
databases. PostgreSQL prepared transactions (2PC) exist, and are rejected: a prepared transaction left
dangling by a crashed coordinator holds locks indefinitely and requires manual DBA intervention, which is
not an operating model for shops with no DBA.
**Decision.** Every cross-database operation is a **saga** coordinated by the registry:
- an immutable **intent** row records what is to happen, in full, before anything happens;
- each **leg** is dispatched through the registry outbox to exactly one company database and is
  **idempotent**, keyed by `(intent_id, leg_id)`, so a retry can never double-apply;
- a leg either acknowledges or is retried with backoff, forever, until it acknowledges or is compensated;
- **compensation is a new document, never a delete or an edit** — a reservation release, a reversing
  journal, a credit-note — consistent with `CLAUDE.md` §7 rule 7;
- an intent whose legs are not all acknowledged inside its timeout raises an alarm with a named owner and
  appears on an in-flight report. Nothing silently gives up.
No command handler may open a transaction against two databases. There is an architecture test that fails
the build if a handler resolves more than one company's `DbContext`.
**Consequences.** The product is eventually consistent across companies and strictly consistent inside
each one, which is the correct split: everything a customer, a cashier or an auditor experiences as
atomic — a sale, an invoice, a receipt, a journal — is inside one database. What is eventually consistent
is the group-level view and the second leg of a cross-company operation, both of which are visible,
reconciled and alarmed. The operational cost is real and is accepted deliberately: in-flight intents must
be monitored, and a period cannot close over one.

## ADR-117 — Migrations fan out across every company database, and a partially migrated tenant is a first-class state — **PROPOSED**
**Context.** One schema now lives in N databases. An upgrade that migrates three of five companies and
then fails must not leave the product guessing which shape it is talking to.
**Decision.** The migration runner iterates every company database in the registry, applying the same
chain, and records `schema_version` and `migration_state` per company in the registry. The registry has
its own separate chain and is always migrated first. A company whose version is behind the running binary
is **not served**: its endpoints return a specific, actionable failure naming the company and the pending
migration, rather than executing against a schema the code does not expect. Migration is resumable and
re-runnable; `Down` is exercised per company database in the test suite exactly as it is today.
**Consequences.** An upgrade can be partial and the system stays truthful about it, which is far better
than a half-upgraded tenant that mostly works. Upgrade time now scales with company count — a hundred
companies is a hundred migrations — so the runner reports progress and can run companies in parallel with
a bounded degree. A company being restored from backup arrives at whatever version it was taken at and is
migrated forward before it is served, by the same path.

## ADR-118 — Creating a company provisions a database, and the lifecycle has no half-registered state — **PROPOSED**
**Context.** "Add a company" is now "create a database". That can fail halfway, and a company that exists
in the registry but has no working database would break every group query that walks the company list.
**Decision.** Provisioning is an explicit lifecycle: `Provisioning` → `Seeding` → `Registered` →
`Active`, recorded in the registry, and a company is only visible to business operations at `Active`. The
steps are: create the database, apply the migration chain, seed the chart of accounts, document numbering,
tax rules and permissions, register the connection, then activate. A failure leaves the company at the
step it reached with the error recorded, and the operation is re-runnable from there. Deactivation makes a
company read-only rather than removing it; removal is a separate, deliberate, exported-first operation.
**Consequences.** Group queries walk `Active` companies only and can never trip over a shell. Connection
details live in the registry rather than in configuration files, so adding a company needs no redeploy —
which also means the registry holds credentials and must be encrypted at rest and excluded from any
support export (`docs/SECURITY.md`). Per-company export before removal falls out of the design for free,
and is exactly what a customer selling one of their businesses will ask for.

## ADR-119 — Group read models live in the registry, are stale by design, and never answer an authoritative question — **PROPOSED**
**Context.** "When placing orders it will show stock across companies in one view, but separated at
transaction level when saved to each company's financials." That view cannot query five databases on
every keystroke, and it must not be the thing that decides what is committed.
**Decision.** Each company database publishes to the registry through its existing outbox: availability
by item, customer exposure, catalogue routing, period figures. The registry serves the unified views —
group stock lookup, group availability, credit position, consolidated reporting — from those projections.
**Every group figure carries `AsAt` and every UI surface shows it.** No group read model is ever the basis
for a commit: reservations re-check in the owning company database (ADR-102), credit consumes in the
registry under serialisation (ADR-101), and postings happen in the owning company database. A projection
is rebuildable on demand by asking each company to republish, and there is a test that the rebuild equals
the incremental projection.
**Consequences.** The single-view experience the operator asked for is fast and survives one company being
briefly unreachable — that company's slice is shown as stale rather than the whole screen failing. The
discipline this ADR really enforces is negative: any code path that reads a registry projection and then
writes a business decision from it, without re-checking in the owning database, is a defect regardless of
how well it appears to work.

## ADR-120 — Backup, restore and sync are per database, and a restored company replays what it missed — **PROPOSED**
**Context.** R4 says a store that burns down comes back from backup. With one database per company, a
restore is now per company — and a company restored to an earlier point may have missed cross-company legs
that the registry believes were delivered.
**Decision.** Snapshots are taken per company database and for the registry, each with its own schedule,
retention and encryption, and the snapshot ledger records the set. A restore of one company database is a
supported operation on its own. After a restore, the registry **re-drives every intent leg** for that
company whose acknowledgement is older than the restore point; because legs are idempotent (ADR-116),
re-driving is safe whether the leg survived the restore or not. The three-tier sync (terminal → store →
cloud) runs per company database with its own outbox, inbox and cursors, and the cloud replica mirrors the
same layout: a registry and N company databases.
**Consequences.** One company can be restored without stopping the others — which is the strongest
practical argument for this whole topology, and worth stating plainly since the costs have been stated at
length. The DR drill in Stage 31 grows a case it did not have: restore one company of three, re-drive the
outstanding legs, and prove the group reconciles afterwards. Backup volume and scheduling scale with
company count, and the registry is the one database whose loss is disproportionate — it is snapshotted
more often than the companies it points at.

## ADR-121 — The Operator ID is issued in the licence and is the only thing that can link two companies — **PROPOSED**
**Context.** Two companies sharing a floor, a till, a credit limit and a customer's payment is a powerful
arrangement, and a dangerous one if anything can be linked to anything. Two unrelated businesses on one
server must not be able to see each other's stock, share a credit facility or issue each other's
invoices — whatever a misconfiguration, a support session or a malicious admin attempts. Something above
the tenant has to say which companies belong together.
**Decision.** The **Operator ID** — the operator's own words were "like a user ID" — is minted by the
vendor control plane (Stage 30b) and **signed into the monthly licence** (Stage 04b). Every company is
provisioned under exactly one Operator ID. A `CompanyLink` may only exist between two companies whose
`operator_id` is identical, enforced in the aggregate **and** by a database check constraint, and the
Operator ID cannot be set by any tenant-side command — it is projected from the signed licence.
**Consequences.** Linking becomes a commercial fact rather than a configuration option, which is what
makes it safe: the vendor knows who owns what, bills accordingly, and can revoke. One tenant may hold
several Operator IDs — a bookkeeper running two clients on one box — and those clients can never be
linked, no matter what anyone configures. The cost is that changing ownership of a company is a
vendor-side operation, not a checkbox; that is the correct place for it.

## ADR-122 — A company link is mutual, scoped, revocable, and checked at the point of use — **PROPOSED**
**Context.** "These two companies are linked" is too coarse. Siyaya and Noortgats share a floor and a
till; Siyaya and Siyaya DC share a credit limit but not a shelf. And a link checked only when it is
created keeps granting access after it is suspended.
**Decision.** `CompanyLink` carries a `[Flags]` scope set — `SharedFloor`, `SharedTill`, `SharedCredit`,
`SharedReceipting`, `SharedSourcing`, `SharedPicking`, `SharedReporting` — and a status machine
(`Proposed → Accepted → Active → Suspended → Revoked`) in which both companies must accept. **Every
cross-company operation calls `ICompanyLinkService.RequireLink(a, b, scope)` at the moment it runs**, and
an architecture test enumerates every cross-company entry point and asserts each one does. Revocation
does not touch documents already created; history stands.
**Consequences.** The blast radius of a mistaken link is exactly one scope, and suspending a link stops
new activity within a cache-invalidation window rather than at the next restart. The enumerating
architecture test is the load-bearing part — without it, the next stage adds an entry point and nobody
notices the missing check until a customer does.

## ADR-123 — Metering is company × named user × till, and a lapsed company drops out of links for writes — **PROPOSED**
**Context.** The operator's commercial model is "pay per company, per user and per till". Linked
companies must not create a way to run six companies' worth of business on one company's licence, and a
single company falling behind on payment must not take a shared floor down with it.
**Decision.** Three metered dimensions with three entitlements: `MaxCompanies`, `MaxUsersPerCompany`,
`MaxTillsPerCompany` (plus `MaxActiveLinks`). A user or till entitled for two companies counts **once in
each**, stated that way on the invoice rather than discovered. Exceeding an entitlement blocks the new
grant with a named error and never disables what already exists. A company whose subscription has lapsed
to read-only (ADR-028) **drops out of every link for writes**: a sister till cannot sell its stock, and
the refusal names the lapse rather than the link. Its own reads, reprints and exports continue.
**Consequences.** The commercial model survives linking, and the failure is proportionate: one company's
billing problem stops that company trading, not the shop. Metering stays counts-only (R10) — company
count, user count, till count, link count, and nothing about turnover.

## ADR-124 — A premises is registry data; a shared floor is many companies' stores at one site — **PROPOSED**
**Context.** Siyaya's and Noortgats' goods sit on the same shelves. A store belongs to exactly one company
(and lives in that company's database), so nothing in the model could express "one building, two
companies" — and a bin code had to mean the same physical shelf to both.
**Decision.** `registry.premises` is the physical site. `registry.premises_occupancies` names one store
per occupying company. The **zone and bin layout is mastered at the premises** and mirrored into each
occupying company's `warehouse` schema, so Stage 13's bins, putaway and pick tasks work unchanged and a
bin code is unambiguous. **A quantity never spans companies** — a shelf may hold both companies' goods;
a stock figure belongs to exactly one. Holding a sister company's stock at a premises requires
`SharedFloor`.
**Consequences.** One walk of the aisle can pick, count or putaway for two companies while every quantity
stays in its own database and its own books. The mirror is a saga leg, so a layout change is eventually
consistent across occupants; a bin created seconds ago in the master may not exist yet in one company,
which fails clearly rather than silently mis-shelving.

## ADR-125 — A mixed basket is a trading session of per-company segments; tax is computed per segment — **PROPOSED**
**Context.** One customer, one till, one payment, two companies' goods. A single document is not legally
possible — each company must issue its own tax invoice under its own VAT number — and computing VAT on
the basket total then splitting it produces a different, wrong number on both invoices.
**Decision.** `TradingSession` holds one `TradingSessionSegment` per company. **Each segment prices,
discounts, taxes and rounds entirely inside its own company's database**; the basket total is the sum of
rounded segment totals, never the rounding of a sum. Promotions apply within a segment only. Completion
produces one complete, independent tax invoice per company, plus a **basket summary** that carries both
invoice numbers and states on its face that it is not a tax invoice.
**Consequences.** Both invoices are legally valid standing alone, and the customer still gets one piece
of paper with one total. The rounding rule is the sharp edge: it is asserted by a test that names the
wrong alternative in a comment, because "simplifying" it back is exactly the kind of tidy-up a later
session would attempt.

## ADR-126 — A mixed-basket tender is a group receipt whose legs are immediate — **PROPOSED**
**Context.** The customer pays R2 113.00 once, and two companies must each record their share. Stage 07c
already built exactly this — capture once, allocate across companies, post per company — for accounts
receivable.
**Decision.** The till's tender allocation **reuses the group receipt machinery** rather than growing a
parallel one: one captured tender, an allocation per segment (proportional by segment total by default,
cashier-overridable, cent-exact with the remainder cent deterministically to the largest segment), each
allocation posting an `finance.ar_receipt` inside its own company's database as a saga leg.
**Consequences.** One implementation of "money captured once, allocated across companies", one set of
reconciliation reports, one place to fix a bug. The difference from a back-office group receipt is only
that the legs are dispatched immediately and the session waits for them — which the saga coordinator
already supports. Cash-up therefore reports per company as well as in total: one drawer, reconciled once,
accountability split.

## ADR-127 — Users are mastered in the registry; roles are per company; the token carries both — **PROPOSED**
**Context.** One person works at a shared floor and must act in two companies without two logins — while
a role in one company must confer nothing in the other, and identity is currently per company database.
**Decision.** `registry.users` is the directory: one row per human, one login, owned by an Operator ID.
`registry.user_company_access` grants roles **per company**. The access token carries the companies the
user may act in and their roles in each; a request naming a company absent from the token is **403**, not
a filtered result. A user may only be granted companies under the same Operator ID.
**Consequences.** One login, correct separation, and an authorisation failure that is honest rather than
one that pretends the data does not exist. Per-company role catalogues stay where they are (Stage 02);
what moved is the directory and the grant. Token size grows with company count, which is bounded by the
`MaxCompanies` entitlement and is not a practical concern at the scales this product targets.

## ADR-128 — A return from a mixed basket credits the origin invoice's company — **PROPOSED**
**Context.** A customer returns a hot plate and a bag of maize from one basket. The hot plate was
Noortgats', the maize was Siyaya's. A single credit note would have to span two companies' books.
**Decision.** A mixed-basket return splits by origin: one credit note per company, each against **that
company's own original invoice**, through Stage 10's existing credit-note path. Attempting to credit a
line against another company's invoice is refused, with both invoice numbers named in the error. There is
no cross-company credit note, in any circumstance.
**Consequences.** The customer hands back one bag of goods and receives two credit notes, which is what
the law requires and what the books require. The refusal message names both invoices because the person
hitting it is a cashier who needs to know which document to use, not a developer.

## ADR-129 — The assistant classifies and phrases; it never computes — **PROPOSED**
**Context.** A conversational assistant that can state a balance, a price or a stock figure is one
hallucination away from telling a customer they owe R4 000 when they owe R40 000. This product's entire
value is that its numbers are right.
**Decision.** The model does exactly two jobs, behind two interfaces: `IIntentClassifier` (message text →
one of six intents + entities + confidence) and `IReplyComposer` (a strict result DTO → prose). **Neither
has data access, tools, or a repository.** Every figure, document, date and balance comes from an API
result, and a post-check asserts that every number and date in composed output appears **verbatim** in
the source DTO — a mismatch falls back to a templated reply and logs a defect. Below a confidence
threshold (default 0.7) the bot shows a keyword menu instead of guessing. If the model provider is
unavailable, the keyword menu is the whole product and it still works.
**Consequences.** The assistant cannot invent a number, and the property test that proves it is the
stage's most important test. The trade is a narrower, more mechanical assistant than a general model
would give — which is the correct trade for something that speaks to customers about money.

## ADR-130 — A conversational requester is bound and verified before anything leaves — **PROPOSED**
**Context.** Statements, PODs and invoices are exactly the documents an attacker wants, and WhatsApp
numbers and email addresses are trivially claimed. "I'm Mkhize Spaza, send my statement" must never work.
**Decision.** A `ContactBinding` is created **by the tenant**, from inside the product, against a specific
contact of a specific customer in a specific company — never by the requester asserting an identity. An
unbound sender receives an onboarding message and no data, and specifically no confirmation that any
account matches the number. Sensitive intents require a **fresh OTP** (valid 10 minutes; re-required after
24 hours of inactivity; three attempts then lockout and a tenant notification). Documents are delivered as
**short-lived signed links** (24 hours, revocable, every fetch audited) rather than as unauthenticated
attachments, unless the tenant chooses otherwise per channel. Scoping is structural: an intent handler's
query cannot express another contact's account.
**Consequences.** Identity failures stop the conversation rather than degrading gracefully, which is the
one place in this product where a hard failure is the friendly behaviour. Rate limits, lockout and the
audit trail make an attack visible to the tenant rather than silent.

## ADR-131 — A bot order lands as a pro forma requiring approval unless the customer is allow-listed — **PROPOSED**
**Context.** An order placed over WhatsApp by a customer is the same thing a rep captures on the road: a
proposal from outside the business, at prices and quantities nobody has checked, possibly against an
exhausted credit limit.
**Decision.** A bot order creates a Stage 14b `ProFormaOrder` and goes through the same approval path
(ADR-107, ADR-108). It posts nothing and reserves nothing until approved. A tenant may allow-list an
individual customer for straight-through ordering; that setting is per customer, off by default, and
audited. The customer is told, in the confirmation, that the order awaits approval.
**Consequences.** One approval path for every proposal channel — rep, bot, and any later one — instead of
a second, weaker one for the channel with the least verification. A tenant who wants speed for a trusted
customer can have it, deliberately and visibly.

## ADR-132 — The assistant owns conversation state; Stage 22 owns transport — **PROPOSED**
**Context.** Stage 22 (marketing automation) already builds WhatsApp and email sending, templates,
opt-out and deliverability. A conversational stage that builds its own would produce two senders, two
opt-out lists and a customer who unsubscribed from one but not the other.
**Decision.** Stage 22b contains **no** WhatsApp client and **no** SMTP client. It owns conversation
state, intent handling and document assembly, and sends through Stage 22's transport. Consent is checked
against Stage 22's opt-out state before every outbound message, and `STOP` on either path stops both.
**Consequences.** One opt-out list, one deliverability reputation, one place where WhatsApp's 24-hour
service window and template rules are implemented. If the transport lacks something the assistant needs,
it is extended in Stage 22 rather than worked around in 22b — and a second client appearing in 22b is a
review finding, not a shortcut.

## ADR-133 — Stage documents are written to be executed by a smaller model — **PROPOSED**
**Context.** The sessions that build this product are not always the model that planned it. A stage
document that leaves a choice open gets a different choice each time it is read, and the cost surfaces two
stages later as an inconsistency nobody can attribute.
**Decision.** `docs/EXECUTION_STANDARD.md` defines the standard, and every stage document from here on
conforms: eight required sections; **every type, file path and test named**; every default, timeout,
expiry and tolerance given as a number; acceptance criteria written as concrete inputs and expected
outputs; an ordered, checkboxed build list whose parts each compile and commit on their own; and an
explicit "what this stage does not own". A document containing the words "decide whether" is unfinished.
Where two designs were considered, the ADR records the rejected one and the stage document carries only
the winner.
**Consequences.** Stage documents are longer and take longer to write, and they are executed the same way
by any session. The standard also makes review cheap: a reviewer checks the built code against named
types and named tests rather than against an interpretation. `STAGE-14-order-management.md` and
`STAGE-06e-trading-group.md` are the worked examples.

## ADR-134 — A bin balance carries a reservation separate from on-hand; a release places it, a confirm or cancel fully clears it — **PROPOSED**
**Context.** §4.19's CRITICAL finding (`docs/PROGRESS.md`, 2026-08-23): `ReleasePickWaveCommand`
computed which bin(s) satisfy a pick line's demand (ADR-090) but never recorded that decision anywhere
— the allocation was advisory, not binding. Two waves released back to back, before either was picked,
could both be allocated against the same on-hand quantity in the same bin, because both reads saw the
same unreserved figure. This is CLAUDE.md §7 rule 21 unmet: *"`Available`, not `OnHand`, answers 'can I
sell this'. A reservation reduces available and leaves on-hand alone... Available may never go negative
in any company under any interleaving."* Nothing before this fix gave `Available` a home to exist in.
**Decision.** `BinStock` gains `QuantityReserved` (mapped alongside `QuantityOnHand`, same shape, same
unit of measure, `Quantity` never nullable) and a computed `Available => QuantityOnHand - QuantityReserved`.
`Reserve(quantity)` throws if it would take `Available` negative, matching `ApplyOut`'s own guard shape.
`LargestBinFirstAllocationStrategy` (ADR-090) ranks and filters candidates on `Available`, not raw
on-hand — a bin another wave has already spoken for genuinely looks smaller, or empty, to the allocator.
A reservation is placed in full at release and **released in full**, not proportionally, at whichever of
three points ends a task's claim on it: `ConfirmPickCommand` releases the task's entire original
`AllocatedQuantity` even on a short pick — the picker found less than expected, but the allocation as a
promise is settled either way, and the unpicked remainder must free up immediately rather than sit as a
phantom reservation until someone notices. `CancelPickTaskCommand` and `CancelPickWaveCommand` both
release an allocated-but-unconfirmed task's reservation too, since a wave may be cancelled after release
while its tasks still hold one — the reservation is state a caller can walk away from mid-lifecycle, and
walking away must not leak it. `ReleaseReservation` clamps rather than throws on an over-release, because
the task's own `AllocatedQuantity` is the source of truth for what it holds; the balance only mirrors it,
and a caller releasing exactly what its own record says it reserved must never fail on a rounding
artifact in the mirror.
**A within-one-command edge case, handled without a second migration or a locking scheme.** Two demand
lines for the same stock-keeping unit inside a *single* `ReleasePickWaveCommand` call are a real
scenario (consolidated picking, the shape Stage 13b formalises) that a persisted reservation alone does
not close: `IBinStockRepository.ListCandidatesAsync` is a read-only snapshot (deliberately not the entity
the caller reserves against — see the next paragraph), so a second line's read within the same call does
not see the first line's reservation until `SaveChangesAsync` runs, well after the handler returns.
`ReleasePickWaveCommandHandler` keeps an in-memory `Dictionary<Guid, Quantity>` of what it has already
reserved this call, by bin, and applies it to each subsequent line's candidate snapshot before the
allocator sees it. No new state persists for this; it exists only for the duration of one handler call.
**A repository-shape finding, worth recording so it is not rediscovered.** The reservation write itself
goes through a plain, tracked `IBinStockRepository.FindAsync(binId, itemId, itemVariantId)` per bin, not
through the objects `ListCandidatesAsync` returns. An earlier draft tried making `ListCandidatesAsync`
itself tracked (dropping its `AsNoTracking()`) so the same objects could be mutated in place; the
reservation silently failed to persist. The query joins `BinStock` against `Bin` in one LINQ expression,
and EF Core's tracking behaviour across a join composed that way did not reliably track the selected side
— `ListCandidatesAsync` stayed `AsNoTracking()`, kept strictly for the allocator's read, and every write
goes through `FindAsync` instead.
**Consequences.** `Available` now exists as a real, persisted figure rather than only as rule 21's prose,
and it is what every future stage reading "can this be sold/picked/promised" should read — `GetBinStockQuery`
already surfaces it. The three release points (confirm, cancel-task, cancel-wave) are the complete set for
Stage 13 as built; a future stage that adds another way to end a task's claim on an allocation (a timeout,
an auto-cancel) must release through the same full-not-proportional rule or reintroduce the leak this ADR
closes. `MoveBinStockCommand` and `ConfirmPutawayCommand` are deliberately untouched — putaway is inbound
and has no advisory/binding gap to close, and an internal transfer moves on-hand, not a reservation.

## ADR-135 — §4.10's read-only carve-outs: a fourth exemption kind for reprints, and two hard deadlines on the in-flight sale/session bound — **LOCKED**
**Context.** `licence-safety`'s 2026-08-15 adjudication of §4.10 (`docs/PROGRESS.md`) found two real gaps
against `LICENSING.md` §4: reprinting an already-completed sale was blocked outright (contradicting Rule
1 and R9's statutory-records position), and the open-session carve-out's machinery (`ISessionScopedCommand`,
`IOpenSessionRegistry`, `ReadOnlyGuardBehaviour`'s branch) was fully built and tested against a fake
command in Stage 04b but never wired to a single real POS command — `OpenSessionRegistry.Open`/`.Close`
were called from nowhere in production code. The adjudication's "trap in the obvious implementation"
warning was the load-bearing finding: wiring only `CompleteSaleCommand`/`CloseTillSessionCommand` is
functionally useless, because `Sale.Complete` throws unless the sale is fully rung and tendered — a
half-rung sale could then be neither finished nor paid for, producing a 422 instead of a 403 and leaving
the drawer stranded exactly as before. And the moment ringing and tendering are also exempted, the
existing bound (`openedAt < restrictedSince`) becomes a hole on its own: nothing stopped a tenant who
simply never closed a session from trading on it forever.
**Decision.**
1. **A fourth `ReadOnlyExemption` kind, `ReceiptReprint`, whose sole member is `RecordReceiptPrintCommand`.**
   Its argument is "cannot originate trade", not "already started": the handler appends one row to an
   append-only log for a sale that already exists, and cannot create a sale, move money, move stock, raise
   a financial event or consume a document number. ADR-052's cap moves from three kinds to four; membership
   stays a closed list asserted by name (`CommandClassificationTests`, `ReadOnlyCorrectnessTests`). Because
   the exemption is unconditional at the guard, `RecordReceiptPrintCommandHandler` enforces its own bound
   directly against `IEnforcementStatusReader`: refused when the referenced sale is still `Open` — that is
   the in-flight case, and belongs to member set below, not to this exemption. This is a second, narrow,
   named exception to the architecture rule that only `VumaRetail.Licensing` may depend on
   `IEnforcementStatusReader` (`LicensingRulesTests.SanctionedEnforcementStatusReaders`), the same shape as
   `IEntitlementService`'s own `CheckLimitAsync` carve-out in that file.
2. **The in-flight carve-out is the ADR-028 session mechanism, not an exemption kind**, and its member set
   is `AddSaleLineCommand`, `VoidSaleLineCommand`, `TenderSaleCommand`, `CompleteSaleCommand`,
   `VoidSaleCommand` and `CloseTillSessionCommand` — every one implementing `ISessionScopedCommand` with
   `SessionId` aliased to `SaleId` (or, for the till close, `TillSessionId`). Deliberately excluded:
   `OpenSaleCommand` and `OpenTillSessionCommand` ("no new sale may start" — the commercial lever),
   `ParkSaleCommand` (parking is how a sale is kept open indefinitely — exempting it would let the bound be
   defeated by simply never un-parking), and `ResumeSaleCommand` — the most important exclusion of the six,
   because parked sales are a pre-existing pool of "already open" sales with no customer at the counter and
   no cash in hand, and exempting Resume would convert that whole pool into a trading allowance the instant
   read-only fell due.
3. **`IOpenSessionRegistry` tracks a sale and its till session as two independent entries**, not one — a
   reservation-style split, the same shape rule 21 already uses for `BinStock` (ADR-134): bounding only the
   session would let one open till licence unlimited new sales for as long as the session stayed open.
   `OpenSaleCommandHandler`/`OpenTillSessionCommandHandler` register unconditionally, whatever the current
   enforcement level, so the registry already knows what was in flight the instant a restriction falls due
   — a carve-out wired in after the fact is a carve-out that ends up half-applied (the exact Stage 04b
   design note this session finally made true). `CompleteSaleCommandHandler` and `VoidSaleCommandHandler`
   close the sale's entry on a genuine (non-replay) completion or void; `CloseTillSessionCommandHandler`
   closes the session's.
4. **Two hard deadlines, `LicensingOptions.InFlightSaleWindow` (30 minutes) and `InFlightCashUpWindow`
   (12 hours)**, published to POS through a new `IOpenSessionWindows` port (`LicensingOptions` implements
   it) so POS never references `VumaRetail.Licensing` directly — the same reason `ITaxCalculator` exists
   rather than POS depending on `VumaRetail.Finance`. `IOpenSessionRegistry.Open` now takes a
   `maxDuration` alongside the timestamp and stores both; `MayCarryOn` takes an `evaluatedAt` and refuses
   once `evaluatedAt - openedAt > maxDuration`, in addition to the existing `openedAt < restrictedSince`
   check — both bounds are required, and either alone is a hole. `evaluatedAt` is `decision.EvaluatedAt`,
   the same watermarked instant `EntitlementService.LoadAsync` computed the enforcement decision from, not
   a fresh `IClock.UtcNow` read at the guard — so winding the system clock back buys nothing here either
   (`LICENSING.md` §7).
**Consequences.** The worst case is exactly what the adjudication specified and no worse: the sales
physically on a till screen at the instant the level flipped finish within 30 minutes, and the cash-ups
open at that instant close within 12 hours; nothing held open past its window, nothing parked and left,
nothing resumed, no new sale or shift, and a store-server restart (the registry is in-memory, per node)
shortens every window further. `ReadOnlyCorrectnessTests` exercises the full member set against the real
POS command types (not a test double — the shape a real command has is exactly what decides whether it is
exempt) both within and past the deadline, plus the clock-wound-back case. One accepted, narrow edge case,
recorded rather than engineered around: a `CompleteSaleCommand` replay that arrives after the sale's
registry entry has already been closed by a genuine first completion, and after read-only has since fallen
due, is refused by the guard with `403` even though the operation is a harmless no-op — the guard cannot
know a request will turn out to be idempotent without running the handler first, which is a general,
pre-existing limit of a pre-handler guard and not unique to this fix. The till's own state is never
wrong — a read always shows the sale as `Completed` — so this is a confusing error on a very late duplicate
retry, not a data or drawer problem.

## ADR-136 — A sale's currency is its till session's, resolved once from the store then the tenant, never taken from the caller — **LOCKED**
**Context.** §4.13: `OpenSaleCommand` accepted an arbitrary client-supplied currency with no check against
the till session, the store or the tenant. A foreign-currency sale — even one immediately voided — sat on
the session until `CloseTillSessionCommandHandler` tried to fold its (zero) `CashContribution` into the
drawer total with `Money`'s raw `+` operator, which throws a plain `InvalidOperationException` on any
currency mismatch regardless of amount. That surfaced as an unhandled `500`, and because `TillSession.Close`
performs its own open-sale check only *after* the aggregation line, the till was left permanently unable to
close the shift — bricked, with no API path out, sometimes triggered by the operator's own correction (void
the wrong-currency sale) rather than by anything malicious.
**Decision.** `Sale.Open` no longer accepts an independently chosen currency: it derives `Currency` from
the `TillSession` it is being opened against and refuses (`PosRuleException.CurrencyMismatch`) if a caller
passes anything else — `OpenSaleCommand` dropped its own `Currency` parameter entirely rather than carry a
field that would silently be ignored. The till session's own currency is resolved once, when it opens, from
`Store.CurrencyOverride` then `Tenant.BaseCurrency` (`OpenTillSessionCommandHandler`, mirroring
`ImportBatchContextFactory`'s identical precedent on the Imports side, which exists for the same reason:
two call sites resolving currency differently is the same bug in a different module) — never from the
request body. `CloseTillSessionCommandHandler` additionally guards the cash aggregation with an explicit
per-sale currency check that throws the same typed exception instead of relying on `Money`'s raw operator,
as a safety net for a state the resolution above should make unreachable.
**Consequences.** A sale can no longer diverge from the till session it is rung up on, and a session can no
longer diverge from the store/tenant's configured currency — the chain store/tenant → session → sale is
enforced by the type signatures involved (`Sale.Open` takes the `TillSession`, not an independent currency
string), which is a stronger guarantee than validating equality at each call site independently. The
specific bricking scenario in §4.13's worked example is now structurally unreachable through the command
API. Multi-currency tendering across a genuine exchange rate remains out of scope for this stage, as
`PosRuleException.CurrencyMismatch`'s own message has always said.

## ADR-137 — `IPermissionChecker`: an in-handler counterpart to `RequirePermission`, for the one write whose legality the endpoint cannot see — **LOCKED**
**Context.** §4.15: `pos.receipt.reprint` is declared `IsHighRisk` in the permission catalogue and enforced
by nothing. `RecordReceiptPrintCommand`'s endpoint is gated on `pos.receipt.print` only, because whether a
given print *is* a reprint is derived from the print log itself (`alreadyPrinted > 0`) — information the
endpoint filter, which runs before the handler and before any row is read, structurally cannot have. No
existing port in the codebase answered "does this user hold this permission" from inside a handler; every
check ran at the endpoint via `RequirePermission`.
**Decision.** A new published port, `IPermissionChecker` (`Application.Abstractions.Identity`), with one
method — `HasPermissionAsync(userId, storeId, permission, ct)` — implemented by `PermissionChecker`
(`Application.Identity`) over the same `IRoleRepository` lookup and `IPermissionCatalogue` filter
`GetEffectivePermissionsQueryHandler` already used for `GET /api/v1/me/permissions`. `RecordReceiptPrintCommandHandler`
calls it only when `alreadyPrinted > 0`, throwing the new `PosForbiddenException.ReceiptReprintNotPermitted()`
(`DomainProblemKind.Forbidden`, `403`) on a `false`.
**Consequences.** This is deliberately a narrow escape hatch, not a general authorization framework inside
handlers — the doc comment on the port says so directly: "a module reaching for this on every write is a
module that should have narrowed its endpoint permission instead." Almost every permission check belongs at
the endpoint and stays there; this exists for the specific, rare shape where a command's legality depends on
state only the handler has read. Stale permissions (a role granted a permission the catalogue no longer
declares) are treated as not held, matching the read path's own behaviour, so a module that is disabled or
renamed cannot leave a phantom grant enforceable.

## ADR-138 — Tax is computed and stored once per document line; nothing downstream recomputes it — **LOCKED**
**Context.** `docs/PROGRESS.md`'s order-of-work note flagged this as cheap to record now and expensive once
Stage 10c's analytics or a VAT201 report starts recomputing tax from a document total and disagreeing with
what was actually posted. The behaviour has been correct since Stage 09 shipped — `AddSaleLineCommandHandler`
calls `ITaxCalculator.CalculateAsync` exactly once, at ring-up, and stores the resulting net/tax/gross on
the `SaleLine` itself (`Net`, `Tax`, `Gross`) — but the decision was never written down, which is exactly
the gap that let §4.14's *rounding* defect survive undetected in the same code path.
**Decision.** Tax is resolved once, at the point a document line is captured, against the rule in effect
that day, and the result is snapshotted onto the line — never recomputed from a line's inputs, a document's
total, or a later reading of the tax rules engine. This already holds for `SaleLine` (Stage 09) and
`PurchaseOrderLine` (Stage 12, via the same `ITaxCalculator` port); every future stage that captures a
priced document line — quotes and invoices (10c), sales returns already follow it by construction since a
return reverses the original line's own figures — must do the same. This is CLAUDE.md §7 rule 26's
"snapshots, not re-derivations" applied specifically to tax, and a restatement of `AddSaleLineCommandHandler`'s
own existing doc comment ("a rate change next month must not restate a receipt a customer is holding")
promoted from a code comment to a locked decision.
**Consequences.** A tenant's VAT return, trial balance and any historical receipt reprint always agree with
each other and with what was actually charged, regardless of rate changes since. The cost is that a line's
stored tax can only be corrected by a new document (a credit note, a reversal) — never by an edit — which is
the same trade-off rule 7 already makes for posted financial documents generally. No consumer — a report, an
analytics query, a reconciliation — may derive tax from `(net, rate)` or `(gross, rate)` on a stored line; it
reads what was stored.

## ADR-139 — Stage 06e correction: trigger not CHECK, registry.* permissions, outbox link events, optional sign-in enrichment — **PROPOSED**
**Context.** The first Stage 06e implementation reached `main` with the shape right and the enforcement
wrong: the operator-match invariant had no `Company.OperatorId` to match against, the "check
constraint" in an early migration draft named SQL PostgreSQL cannot parse (`IN` over two `SELECT`s —
verified never created in any database, removed from the migration metadata), permission keys broke
ADR-013's three-segment rule, acceptance recorded nothing about who or under which licence, terminal
handlers were stubs, and four architecture tests failed.
**Decision.** (1) ADR-121's database half is a `BEFORE INSERT OR UPDATE` trigger on
`registry.company_links`, not a `CHECK` — a check constraint cannot reference another table, and an
invalid one is worse than none because the next migration diffs it forever. The aggregate and
`ProposeAsync` refuse first; the trigger refuses a row that reaches the database anyway. (2) The
stage text's two-segment `grouplink.*` permission names become `registry.grouplink.*` (plus
`registry.premises.manage`, `registry.user.manage`, `registry.terminal.manage`) — ADR-013 requires
`module.entity.action` and the module is `registry`. (3) Link mutations publish a durable
`company-link.changed` registry outbox row and invalidate the local snapshot cache in the same
transaction; there is no SignalR transport anywhere in this product, so cross-node invalidation
rides the outbox channel a transport can subscribe to later rather than an invented one now.
(4) Sign-in enriches the token with registry membership through the optional
`ITokenCompanyEnricher` seam — optional so pre-registry logins and existing constructions keep
working unchanged. The signed licence does not carry the Operator ID yet (Stage 04b never minted
it); until it does, the operator is projected from the registry row the licence fingerprint points
at, and changing that projection to read the licence is a small, named follow-up, not a rework.
**Consequences.** The invariant holds at three levels (aggregate, service, trigger) with each level
documenting why the next one exists. Permission keys parse. Link refusals carry the stable problem
type `https://vuma.dev/problems/company-link-required` with both companies and the missing scope.
The cost accepted: cross-node cache invalidation within one second (the stage's acceptance test)
cannot pass until a transport exists, and the three-company seed fixture cannot run until Stage 06c's
provisioning runtime is wired to a secret store — both recorded as deferred in `docs/PROGRESS.md`,
neither faked.

## ADR-140 — Database-enforced rules answer coded refusals; both databases migrate everywhere; sign-in survives a degraded registry — **PROPOSED**
**Context.** A CI run failed every API integration test at sign-in with a 500, and the log bundle
mixed three unrelated causes: a missing database/schema, benign constraint noise from negative
tests, and genuine 500s. Tracing showed (1) the test template migrated only the business context,
so every registry read in a test database answered "relation does not exist" — including the new
sign-in enrichment, which runs on every login; (2) the host's `--migrate` path migrated only the
business context, so a fresh deployment had the same hole; (3) a PostgreSQL check or unique
violation reaching the API edge fell through to 500 with no stable code.
**Decision.** (1) The test template and the host migrate **both** contexts — a test database serves
the full host, and a deployment is not migrated until both databases are. (2) Check violations
(SQL state 23514) answer 422 `CHECK_VIOLATION` and unique violations (23505) answer 409
`UNIQUE_VIOLATION`, each carrying the firing constraint's name as an extension — the constraint is
the last line of defence behind the domain, and reaching it means a rule with no coded refusal yet,
which is still a refusal, not a crash. (3) Sign-in enrichment degrades to the pre-registry token
when the directory is unreachable: sign-in is an edge service in ADR-040's sense, and a degraded
registry must cost company claims, not logins.
**Consequences.** The CI failure's three causes are separated for good: setup gaps close by
construction, negative-test constraint noise stays server-side log noise (the tests assert the
refusal), and no database-enforced rule can surface as a 500 again. The accepted cost is that a
down registry silently narrows tokens until it recovers — visible in the server logs, and the
correct side to fail on: authentication first, enrichment second.
