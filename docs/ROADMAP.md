# ROADMAP — Vuma Retail

46 stages, dependency-ordered. Each stage is decomposed into focused task sessions and ends at a green
build with the work demonstrable. Close the task session, open a new one, repeat.

> **Revision 4 (2026-08-22).** Nine stages were inserted — **06c**, **06d**, **07c**, **08c**, **13b** and
> **14b** — for multi-company operation, cross-company money, availability and reservations, consolidated
> picking, and the field-sales (rep) module. **Each company gets its own database** (ADR-099, revised the
> same day at the operator's instruction after the first form of that ADR proposed logical separation). Stages **10c** and **14** were amended rather than
> renumbered. See ADR-099 – ADR-115, `docs/MULTI_COMPANY.md` and `docs/FIELD_SALES.md`. Nothing already
> shipped was removed or renumbered.

> **Revised.** The original 31-stage plan (00–31, no letters) was missing a design system, a
> customer-money module and the supplier network. Stages **08b**, **10b** and **21b** were inserted
> — see ADR-055 through ADR-058 — rather than renumbering work already built. Nothing already shipped
> was removed or renumbered.

Stage numbers here are authoritative; the module → stage mapping in `CLAUDE.md` §6 must agree with
this table. Live status is tracked in `docs/PROGRESS.md`, not here — this table is the plan, that file
is the truth, and if they disagree `PROGRESS.md` wins and this table gets corrected.

Each stage gets a document at `docs/stages/STAGE-NN-<slug>.md` before it is executed. Where the
document does not exist yet, the executing session writes it first — objective, deliverables,
business rules, tests/acceptance, exit checklist — using `STAGE-04b-licensing.md` as the template. A
linked title below means the document exists on `main`; plain text means it doesn't yet.

## Phase A — Platform (00–05)

Nothing user-visible ships here, but every later stage depends on it. Do not rush it.

| # | Stage | Depends on | Why it matters |
|---|---|---|---|
| 00 | [Foundation & Tooling](stages/STAGE-00-foundation.md) | — | Solution skeleton, CI, conventions enforced by analysers |
| 01 | [Persistence Core](stages/STAGE-01-persistence.md) | 00 | Base entity, tenancy, UUIDv7, soft delete, outbox, CQRS pipeline |
| 02 | [Identity, RBAC & Audit](stages/STAGE-02-identity.md) | 01 | Users, roles, permissions, device registration, POS PIN, audit trail |
| 03 | [API Platform & Realtime](stages/STAGE-03-api-platform.md) | 02 | Versioned REST, OpenAPI, ProblemDetails, idempotency, CQRS pipeline |
| 04 | [Sync Engine, Cloud Backup & Restore](stages/STAGE-04-sync-and-backup.md) | 03 | Three-tier sync, HLC, conflict policy, encrypted snapshots, restore tooling |
| 04b | [Licensing, Activation & Entitlement](stages/STAGE-04b-licensing.md) ★ | 04 | Monthly signed licence, hardware activation, entitlement gating, enforcement ladder |
| 05 | Workflow, Approvals, Notifications & Documents ★ | 04b | The generic engines a dozen later modules would otherwise each reinvent — **WIP, branch `stage-05-workflow`, not yet merged** |

## Phase B — Financial & retail core (06–11)
The minimum that makes a business able to trade *and* account for it.

| # | Stage | Depends on | Why it matters |
|---|---|---|---|
| 06 | Master Data | 05 | Products, variants, barcodes, UoM, price lists, tax rules, customers, suppliers — **WIP, branch `worktree-stage-06-master-data`, not yet merged** |
| 06c | [Multi-Company Foundation](stages/STAGE-06c-multi-company.md) ◆ | 01, 04, 06, 07 | **One database per company** plus a tenant registry: connection routing, provisioning lifecycle, migration fan-out, per-database backup and sync |
| 06d | [Group Services](stages/STAGE-06d-group-services.md) ◆ | 06c | The saga coordinator, credit groups with hold tokens, the barcode routing index, the group read models — everything that spans databases |
| 06e | [Trading Group](stages/STAGE-06e-trading-group.md) ◆ | 04b, 06c, 06d | **The Operator ID and the company link** — which companies may cooperate at all, scoped and checked at every point of use; shared premises; cross-company users and tills; the billing dimensions |
| 07 | Financial Core: GL, AR, AP, Banking, Tax ★ | 06 | The accounting spine. Without it this is not an ERP — **DONE, merged to `main` 2026-08-15** |
| 07c | [Cross-Company Money](stages/STAGE-07c-cross-company-money.md) ◆ | 07, 06d | Capture one receipt, allocate it across companies; inter-company clearing; per-company statements plus a labelled consolidated view |
| 08 | [Inventory Core](stages/STAGE-08-inventory-core.md) | 07 | Append-only stock ledger, valuation, adjustments, transfers, stocktakes — **DONE, merged to `main` 2026-08-15** |
| 08b | [Design System & Theming](stages/STAGE-08b-design-system.md) ★ | 08 | Apple-inspired tokens, dark + light, component library — built before any UI |
| 08c | [Cross-Company Availability, Reservations & Split Fulfilment](stages/STAGE-08c-availability-sourcing.md) ◆ | 08, 06d | The reservation ledger, available-to-promise, sourcing across companies without ever going negative, one order → several invoices |
| 09 | [POS Terminal & Hardware](stages/STAGE-09-pos-terminal.md) | 08 | The till: sales, tenders, receipts, cash-up, printer/drawer/scanner — API and hardware merged to `main` 2026-08-15; the WPF till screen is deferred with 08b. **REOPENED after the agent reviews of 2026-08-15: `stage-verifier` returned `STAGE NOT DONE — 2 failing, 3 unverified`. Eight open defects, see `PROGRESS.md` §4.10–§4.16** |
| 09b | [The Mixed Basket](stages/STAGE-09b-mixed-basket.md) ◆ | 09, 06e, 07c, 08c, 10c | One till, one customer, two companies' goods → **one tax invoice per company**, taxed and rounded per company, one payment allocated across them, one non-fiscal basket summary |
| 10 | [Sales Management & Promotions](stages/STAGE-10-sales-promotions.md) | 09 | Price lists and price resolution, the promotions engine, returns and credit notes — **DONE, merged to `main` 2026-08-16**. Split as ADR-074; quotes, invoices and analytics moved to 10c |
| 10c | Quotes, Invoices & Sales Analytics | 10, 14, 08c | Sales *documents* and their reporting. Split out of 10 by ADR-074 — nothing in 10b, 11 or 12 depends on it. **Amended (Rev 4):** invoice lines carry and print **pack sizes** (ADR-112), and an order sourced across companies produces one invoice per company (ADR-102) |
| 10b | [Customer Accounts, Lay-by & Stokvels](stages/STAGE-10b-accounts-layby-stokvel.md) ★ | 07, 09, 10 | Credit accounts, lay-by, stokvel groups — money held on behalf of customers |
| 11 | [Data Import (Excel/CSV/PDF)](stages/STAGE-11-data-import.md) | 06 | Ingest suppliers, customers, inventory and specials with mapping, preview, validation and rollback — **DONE 2026-08-16**, branch `stage-11-data-import`. See `docs/IMPORT_PIPELINE.md`. Closes `PROGRESS.md` §4.16. **The six agent reviews have not run (§4.17)** |

## Phase C — Supply chain (12–18)

| # | Stage | Depends on | Why it matters |
|---|---|---|---|
| 12 | [Procurement](stages/STAGE-12-procurement.md) | 08, 11 | Requisitions, RFQs, POs, GRNs, three-way match, supplier scorecards — **DONE 2026-08-17**, merged to `main`. Closes the "minimum viable trading system" at 00–12: the shop can now buy as well as sell |
| 13 | Warehouse Management | 08 | Zones/bins, putaway, pick/pack/ship, cycle counts, handheld flows — **DONE 2026-08-19**, merged to `main` |
| 13b | [Consolidated Picking Waves, Staging States & Interval Counts](stages/STAGE-13b-picking-waves-staging.md) ◆ | 13, 14 | Pick a whole town in one walk: waves by period + province/city/suburb grouped by SKU; consolidation/packing/dispatch as bins; scheduled counts of slow movers that warn when stock is in flight |
| 14 | Order Management | 10, 13, 08c | Omnichannel orders, allocation, backorders, click & collect, returns — **IN_PROGRESS**, branch `stage-14-order-management`. **Amended (Rev 4):** COD settlement terms enforced at dispatch (ADR-111), geography snapshotted on the order for 13b's waves (ADR-113), and allocation goes through 08c's reservation ledger rather than a local one (ADR-103) |
| 14b | [Field Sales — the Rep Module](stages/STAGE-14b-field-sales.md) ◆ | 14, 10c, 08c, 05 | Reps on the road: pro forma orders and credit notes, live availability, management approval before anything becomes an invoice, rep performance per month with comparison |
| 15 | Merchandise Planning, Forecasting & Replenishment ★ | 12, 13, 14 | Forecasting, MRP/DRP, safety stock, open-to-buy, markdown planning |
| 16 | BOM Setup | 06 | Multi-level BOMs, versions, alternates, routings, rolled-up costing |
| 17 | Manufacturing | 16, 13 | Work orders, issue/receipt, WIP, scrap, capacity, genealogy |
| 18 | Quality Management ★ | 12, 17 | Inspection plans, NCR/CAPA, shelf life, certificates, recall management |

## Phase D — Customer & channels (19–24)

| # | Stage | Depends on | Why it matters |
|---|---|---|---|
| 19 | CRM | 10 | 360° customer view, leads, opportunities, activities, segments, consent |
| 20 | Loyalty Programme & Public API ★ | 10, 19 | Earn/burn/tiers/rewards as a real module with an external API |
| 21 | Ecommerce, Storefront API & Channels ★ | 14, 20 | Headless commerce API, PIM content, marketplace and EDI connectors |
| 21b | [Vuma Connect: Supplier Network & B2B](stages/STAGE-21b-vuma-connect.md) ★ | 12, 14, 21, 07 | Suppliers publish prices, retailers order and pay in-app — the ecosystem |
| 22 | Marketing Automation | 19 | Campaigns, journeys, email/SMS/WhatsApp, specials targeting, attribution |
| 22b | [Conversational Commerce](stages/STAGE-22b-conversational-commerce.md) ◆ | 22, 19, 14b, 10c, 07, 24 | The **WhatsApp and email assistant**: takes orders, sends statements, invoice copies, PODs and credit notes. Six intents, verified identity, and a model that classifies and phrases but never computes |
| 23 | Service Management | 14 | Tickets, repairs/RMA, warranties, SLAs, service jobs and parts |
| 24 | Logistics Management | 14, 13 | Shipments, carriers, routes, delivery runs, PODs, tracking |

## Phase E — People & assets (25–28)

| # | Stage | Depends on | Why it matters |
|---|---|---|---|
| 25 | HR Management | 02 | Employees, contracts, leave, documents, disciplinary, payroll export |
| 26 | Workforce Management | 25, 09 | Rosters, shift swaps, clock in/out, labour cost vs sales, compliance |
| 27 | Assets, Maintenance & Store Operations ★ | 07, 25 | Fixed assets + depreciation, CMMS, leases, store tasks and compliance |
| 28 | Projects, Contracts & Job Costing ★ | 07, 26 | Job costing, WIP, milestone billing, contract and rebate management |

## Phase F — Intelligence & release (29–31)

| # | Stage | Depends on | Why it matters |
|---|---|---|---|
| 29 | Reporting & Admin Dashboard API | all module stages | Read models, KPI cube, scheduled reports, the API the Android app consumes |
| 30 | Android Admin App | 29 | Kotlin/Compose dashboards, approvals, stock lookup, push alerts |
| 30b | [Vendor Control Plane, Metering & SaaS Billing](stages/STAGE-30b-control-plane.md) ★ | 04b, 29, 30 | Licence issuance, usage analytics, subscription billing, abuse queue, vendor console |
| 31 | Hardening, DR, Packaging & Release | all | Security pass, load test, DR drill, installer, auto-update, docs, handover |

★ = added in the Revision 3 gap-closing pass (ADR-055 – ADR-059).

◆ = added in Revision 4, 2026-08-22 (ADR-099 – ADR-133) — multi-company operation, the trading group and
shared floors, field sales, and the conversational assistant.

---

## Dependency graph (text)

```
00 ─ 01 ─ 02 ─ 03 ─ 04 ─ 04b ─ 05 ─ 06 ─ 07 ─ 06c ─ 06d ─┬─ 06e ─┐
                                                           └─ 07c  │
                                                                   │
   06d ─ 08 ─ 08c ─┬─ 08b ─ 09 ──────────────┬─ 09b ◄────────────────┘
                   │                         │   ▲
                   │           09 ─ 10 ─┬─ 10c ───┘
                   │                    └─ 10b
                   └─ 14 ─┬─ 13 ─ 13b
                          ├─ 10c ─ 14b
                          ├─ 21 ─ 21b ─ 22 ─ 22b ◄─ 24
                          ├─ 23
                          └─ 24
   08 ─┬─ 12 ─ 15
       ├─ 11 ─ 12
       ├─ 16 ─ 17 ─ 18
       ├─ 19 ─ 20 ─ 21 ─ 22b
       └─ 27, 28
                            02 ─ 25 ─ 26
              (all modules) ─ 29 ─ 30 ─ 30b ─ 31
                        04b ─┬───────────┘
                             └─ 06e
```

**06c is the one that must not be deferred.** It changes where every module's `DbContext` comes from.
Every stage after it writes business tables and opens database connections, and retrofitting
one-database-per-company across forty modules later is not a refactor, it is a rewrite. 06d follows
immediately because 07c, 08c, 13b and 14b all dispatch through its saga coordinator.

## Minimum viable trading system

If you need something usable before the whole plan is finished, stages **00–11** (including **04b**
and **08b**) give a store that can price, sell, take money, track stock, keep books, import its
opening data, present a themed UI and be licensed. Add **12** and it can also buy. That is the
earliest sensible pilot point — and because 04b is in it, that pilot can be a paying customer rather
than a giveaway.

## Ordering notes

- **07 before 08 before 09.** Finance sits ahead of Inventory and POS because those modules post to
  it (ADR-016). Building POS first means retrofitting the ledger into a live sale path.
- **05 before every module.** Approvals, notifications and documents are built once, up front, so
  later stages configure rather than build (ADR-019).
- **04b before 06.** Every module stage from 06 onward gates on entitlements and reports usage, so
  the entitlement choke point must already exist.
- **08b before 09 — for the screen, not for the stage.** ADR-058 still holds: the POS terminal is the
  first screen built and must consume generated tokens rather than invent its own. What Stage 09
  actually shipped is the till's API, domain and hardware layer, which no design system gates and
  which R3 requires to exist before any UI anyway. 08b plus `VumaRetail.Desktop` render it. Neither
  can be built on the Linux machine this repository is developed on (ADR-031, `PROGRESS.md` §4.3), so
  09 was taken as far as it goes without Windows rather than blocked behind a stage that is also
  blocked.
- **10b after 07, 09 and 10.** Accounts, lay-by and stokvels post to Finance and are sold and paid for
  through the POS and Sales flows those stages build (ADR-055). **Lay-by is 10b's, not 09's** — this
  table's one-line summary for Stage 09 used to say "lay-by" and `CLAUDE.md` §6 used to promise a
  "09 addendum (lay-by, self-checkout)". Both predate ADR-055, which gave money held on behalf of a
  customer its own module. Corrected in Stage 09: the till declares the `CustomerAccount` tender type
  and settles nothing behind it.
- **20 and 21 late but together.** Both sit on `VumaRetail.PublicApi` with its own DTOs and auth
  model (ADR-021), and 21 depends on 20's member identity.
- **21b after 21.** Vuma Connect is a trading network on top of ecommerce's headless API and Finance's
  posting rules (ADR-056, ADR-057).
- **31 last.** The DR drill (R4) must exercise a system that is actually complete.
- **06c and 06d before everything that follows them.** 06c makes each company its own database and
  routes every context through the registry; 06d builds the saga coordinator, the credit group, the
  routing index and the group read models that 07c, 08c, 13b and 14b all consume. They are split because
  06c is plumbing that must be boring and complete before anything interesting sits on it. Doing them now
  costs two stages of retrofit across thirteen shipped ones; doing them after Stage 21 costs a rewrite
  (ADR-099, ADR-116).
- **13b after 14, not after 13.** A consolidated pick wave groups *orders*, and Stage 14 is what makes
  orders exist. Stage 13's `PickTask.OutboundReference` is already the seam; 13b adds the wave builder
  that fills it, and does not fork the pick model.
- **06e straight after 06d, and before anything that trades across companies.** 06d gives companies the
  ability to cooperate; 06e decides which ones are *allowed* to, and wires the check into every
  cross-company entry point. Building 07c, 08c, 13b or 14b before it means retrofitting a permission
  check into paths that already work without one — which is how a check gets missed (ADR-122).
- **09b needs Stage 09's open defects fixed first.** The mixed basket doubles the blast radius of
  §4.11's non-idempotent replay: one bad replay becomes two companies' books wrong instead of one. The
  stage document says so in its first build-list item.
- **22b after 22, and it contains no transport of its own.** Stage 22 already builds WhatsApp and email
  sending, templates and opt-out. A second sender would mean two opt-out lists and a customer who
  unsubscribed from one but not the other (ADR-132). If 24 has not landed, 22b ships every intent except
  POD and records the deferral.
- **14b last of the new five.** The rep module is a thin, high-value layer over 08c's availability,
  14's orders, 10c's invoices and 05's approvals. Built before them it would have to fake all four.
- **10c and 14 are amended, not renumbered.** Pack size on the invoice, COD on the order, geography on
  the order, reservations through 08c. Both stage documents carry the amendments; whoever executes them
  reads `docs/MULTI_COMPANY.md` and ADR-111, ADR-112, ADR-113 first.

## Rules of engagement

- Complete stages **in numeric order** unless the dependency graph clearly permits otherwise.
- A stage is DONE only when its own Exit Checklist *and* the global Definition of Done in
  `CLAUDE.md` §8 both pass.
- Every stage ships: migrations, API endpoints, tests, seed data, permissions, sync registration,
  financial posting rules where money moves, **entitlement flags and metering counters**, and desktop
  UI where the stage says so.
- If a stage is too big for one session, split it into `STAGE-NN` and `STAGE-NNb`, update this file
  and `PROGRESS.md`, and carry on. Do not silently under-deliver.
