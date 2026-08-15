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
