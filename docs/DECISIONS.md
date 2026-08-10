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
