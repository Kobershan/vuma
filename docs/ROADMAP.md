# ROADMAP — Vuma Retail

37 stages, dependency-ordered. Each stage is sized for one focused Claude Code session and ends at a
green build with the work demonstrable. Close the chat, open a new one, repeat.

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
| 07 | Financial Core: GL, AR, AP, Banking, Tax ★ | 06 | The accounting spine. Without it this is not an ERP — **DONE, merged to `main` 2026-08-15** |
| 08 | Inventory Core | 07 | Append-only stock ledger, valuation, adjustments, transfers, stocktakes |
| 08b | [Design System & Theming](stages/STAGE-08b-design-system.md) ★ | 08 | Apple-inspired tokens, dark + light, component library — built before any UI |
| 09 | POS Terminal & Hardware | 08b | The till: offline sales, tenders, receipts, lay-by, cash-up |
| 10 | Sales Management & Promotions | 09 | Quotes, invoices, returns, specials engine, sales analytics |
| 10b | [Customer Accounts, Lay-by & Stokvels](stages/STAGE-10b-accounts-layby-stokvel.md) ★ | 07, 09, 10 | Credit accounts, lay-by, stokvel groups — money held on behalf of customers |
| 11 | Data Import (Excel/CSV/PDF) | 06 | Ingest suppliers, customers, inventory and specials with preview + rollback |

## Phase C — Supply chain (12–18)

| # | Stage | Depends on | Why it matters |
|---|---|---|---|
| 12 | Procurement | 08, 11 | Requisitions, RFQs, POs, GRNs, three-way match, supplier scorecards |
| 13 | Warehouse Management | 08 | Zones/bins, putaway, pick/pack/ship, cycle counts, handheld flows |
| 14 | Order Management | 10, 13 | Omnichannel orders, allocation, backorders, click & collect, returns |
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

---

## Dependency graph (text)

```
00 ─ 01 ─ 02 ─ 03 ─ 04 ─ 04b ─ 05 ─ 06 ─ 07 ─┬─ 08 ─ 08b ─┬─ 09 ─ 10 ─┬─ 14 ─┬─ 21 ─ 21b ─ 22
                                        │           │           │      ├─ 23
                                        │           ├─ 12 ─ 15   │      └─ 24
                                        │           └─ 13 ───────┘
                                        ├─ 11 ─ 12
                                        ├─ 16 ─ 17 ─ 18
                                        ├─ 19 ─ 20 ─ 21
                                        └─ 27, 28
                            02 ─ 25 ─ 26
              (all modules) ─ 29 ─ 30 ─ 30b ─ 31
                        04b ─────────────┘
```

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
- **08b before 09.** The POS terminal is the first screen built; it must consume generated tokens and
  components rather than invent its own (ADR-058).
- **10b after 07, 09 and 10.** Accounts, lay-by and stokvels post to Finance and are sold and paid for
  through the POS and Sales flows those stages build (ADR-055).
- **20 and 21 late but together.** Both sit on `VumaRetail.PublicApi` with its own DTOs and auth
  model (ADR-021), and 21 depends on 20's member identity.
- **21b after 21.** Vuma Connect is a trading network on top of ecommerce's headless API and Finance's
  posting rules (ADR-056, ADR-057).
- **31 last.** The DR drill (R4) must exercise a system that is actually complete.

## Rules of engagement

- Complete stages **in numeric order** unless the dependency graph clearly permits otherwise.
- A stage is DONE only when its own Exit Checklist *and* the global Definition of Done in
  `CLAUDE.md` §8 both pass.
- Every stage ships: migrations, API endpoints, tests, seed data, permissions, sync registration,
  financial posting rules where money moves, **entitlement flags and metering counters**, and desktop
  UI where the stage says so.
- If a stage is too big for one session, split it into `STAGE-NN` and `STAGE-NNb`, update this file
  and `PROGRESS.md`, and carry on. Do not silently under-deliver.
