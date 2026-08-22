# MULTI-COMPANY — running several trading companies, each in its own database

> **The requirement, in the operator's words.** "Each company will have their own database. As you scan
> items they should check which company it belongs to and create separate invoices for them. When
> placing orders it will show stock across companies in one view, but will be separated at transaction
> level when being saved to each company's financials." And: one credit limit spread across companies,
> for customers and suppliers; one receipt captured and allocated across companies; financials per
> company.

This document is the contract for that. It is referenced by Stages **06c**, **07c**, **08c**, **10c**,
**13b** and **14b**, by requirements **R11** and **R12** in `CLAUDE.md` §3, and by ADR-099 – ADR-106 and
ADR-116 – ADR-120.

---

## 1. The topology

```
TENANT  (one customer of Vuma Retail — one licence, one subscription)
│
├── REGISTRY DATABASE   vuma_<tenant>_registry
│     companies + their connection details      credit groups + exposure ledger
│     company groups                            catalogue routing index (barcode → company)
│     group receipts / payments + allocations    group availability read model
│     saga intents + outbox + in-flight state    consolidated period figures
│
├── COMPANY DATABASE    vuma_<tenant>_siyaya_hardware
│     the whole Vuma schema: catalog, partners, finance, inventory, pos, sales,
│     procurement, warehouse, orders, fieldsales — its own GL, its own numbering,
│     its own stock, its own outbox, its own backups
│
├── COMPANY DATABASE    vuma_<tenant>_siyaya_dc
└── COMPANY DATABASE    vuma_<tenant>_trade_rite
```

Every company database carries **the same schema and the same migration chain**. The registry has its
own. A store may host several companies' databases on one PostgreSQL instance; nothing assumes it, and
one company can be moved to its own instance by changing its connection details in the registry
(ADR-118).

**A store belongs to exactly one company** and lives in that company's database. A company may have many
stores.

### The three rules that follow from this and are never bent

1. **There is no cross-database transaction.** Not two-phase commit, not `postgres_fdw`, not a clever
   wrapper. Anything spanning companies is a saga (ADR-116).
2. **Strict consistency inside a company; eventual consistency across them.** Everything a cashier, a
   customer or an auditor experiences as atomic — a sale, an invoice, a receipt, a journal, a stock
   movement — happens inside one database. What is eventually consistent is the group *view* and the
   second leg of a cross-company operation, and both are visible, reconciled and alarmed.
3. **A group read model never decides a commit.** It plans; the owning company database commits
   (ADR-119).

---

## 2. How the code reaches a database

| Seam | What it does |
|---|---|
| `ICompanyConnectionResolver` | company id → connection details, from the registry, cached, refreshed on change. Never from a config file. |
| `ICompanyDbContextFactory` | opens a `VumaRetailDbContext` against **one** company. Every command handler uses exactly one. |
| `IRegistryDbContext` | the registry. Group containers, credit, routing, intents, read models. |
| `ICompanyFanOut` | bounded-parallelism read across several company databases, for the rare case where a live figure is needed rather than a projection. Returns per-company results **including failures** — a fan-out where one company is down returns four answers and one error, never a 500. |
| `ISagaCoordinator` | writes an intent, dispatches idempotent legs, tracks acknowledgement, compensates, alarms. |

**An architecture test fails the build if one command handler resolves two companies' `DbContext`s.**
That test is the enforcement behind rule 1 above; everything else is convention.

`company_id` is still a column on every business row, even though the database already implies it. It
costs nothing, it makes a restored or exported database self-describing, and it is what the group
projections are keyed by.

---

## 3. Scanning across companies

```
scan "6001234567890"
  → registry.catalog_routing_index  (tenant_id, barcode)
        → company: Trade Rite Groceries, item, variant, pack size
  → read the item detail from the Trade Rite database
  → { company: "Trade Rite Groceries", item: "Sugar 2.5kg", packSize: 6, available: 143, asAt: 09:41 }
```

- **One indexed probe, then one company read** — not a query per company (ADR-100).
- The routing index is a **projection**, published by each company database's outbox whenever a barcode
  is created, changed or retired, and rebuildable by asking every company to republish.
- **Collisions resolve, never guess:** the terminal session's company, then the company with available
  stock at the scanning location, then `MultipleCompanyMatches` with every candidate for the operator.
- **Eventual consistency is disclosed.** A barcode created in another company seconds ago may not resolve
  yet; the fallback is the scanning company's own catalogue and the operator is told the lookup was local.
- **Registry down → the till keeps trading** on its own company (R1 outranks group convenience). It
  simply cannot sell a sister company's stock until the registry is back, and it says so.

---

## 4. One view of stock, separated at transaction level

This is the operator's sentence made concrete.

**The view** — group availability from the registry projection, one row per item per company, each with
its `AsAt`. A company that has not published recently is shown as stale rather than silently summed.

**The transaction** — sourcing is two steps, and the second is authoritative (ADR-102):

```
1. PLAN     from registry projection      "Hardware 12, DC 30, Trade Rite 0"   ← may be stale
2. COMMIT   local serialisable transaction in EACH named company's own database
            re-reads that company's real availability and reserves what is actually there
            short leg → re-source once → still short → backorder line
```

Stale group data can therefore cause a **re-plan**; it can never cause a negative, because nothing
commits a company's stock except a transaction inside that company's own database. That is the single
correctness argument this design rests on, and the test for it feeds the planner a deliberately stale
projection and asserts a backorder rather than an oversell.

**Reservations are local** (ADR-103): an order drawing on two companies holds two reservations, one in
each database, tied together only by the group document reference. `Available = OnHand − Reserved −
InStaging`, computed per company, always.

---

## 5. One order, one invoice per company

```
SO-2026-000412  captured against Trade Rite, customer Mkhize Spaza
 ├── Trade Rite database   → INV-TR-000188   3 lines   R 4 210.00
 └── Hardware database     → INV-SH-000094   1 line    R 1 899.00
       both carry GroupDocumentRef = SO-2026-000412
```

- Each invoice is written **entirely inside its own company's database**, by that company's numbering,
  posting rules, GL and AR sub-ledger. Nothing about it is shared.
- The customer receives one delivery and two invoices. Picking, packing and the delivery run consolidate
  across companies (Stage 13b); the money never does.
- The split is by **seller**, so each company sells only its own stock — there is no inter-company sale
  to unwind and no transfer pricing to invent.
- `ISplitDocumentBuilder` asserts the split reconciles to the source line for line and cent for cent,
  and that every line lands on exactly one invoice.
- A credit note reverses inside its own invoice's company. There is no cross-company credit note.
- COD applies per order and is inherited by every invoice it produces (ADR-111).

---

## 6. Credit limits across companies

> "A user has three companies and each company has a client — that client must have a total credit
> available of 150k, spread over all companies. Same with suppliers."

The limit lives in the **registry**; the invoice lives in a **company database**; they cannot commit
together. So credit is consumed by a **hold token** (ADR-101):

```
1. registry, serialisable:  check group limit → issue HOLD  (amount, company, doc ref, expires 15 min)
2. company database:        write the document carrying the hold token
3. company outbox:          confirm consumption to the registry, idempotently
   …never got to step 2?    the hold EXPIRES and the credit comes back
```

```
Credit group "Mkhize Spaza Group"        limit R150 000
 ├── Trade Rite       confirmed R62 400   sub-limit R100 000
 ├── Siyaya Hardware  confirmed R31 000   sub-limit none
 ├── Siyaya DC        confirmed R 9 800   sub-limit R 40 000
 └── unexpired holds  R 4 000
Group exposure R107 200 · available R42 800
```

- Exposure = confirmed consumption **plus unexpired holds**, so it is never inflated by a document that
  does not exist and never understated while one is being written.
- Two tills in two companies cannot both take the last of a limit: the second serialises and is refused.
  There is a concurrency test against a real registry database — a mock cannot fail the way this fails.
- A member sub-limit narrows; it can never widen the group ceiling.
- **Suppliers are the same object with the direction reversed** — one facility, capped across every
  company that buys on it.
- **Registry unreachable → credit sales refused, cash sales continue.** A store that cannot reach the
  ledger of record must not grant credit against it.
- A held-but-unconfirmed report is an operational necessity, not a nicety.

---

## 7. Receipting across companies

> "Capture the total receipted, then allocate it — 1k to Siyaya, 3k to Siyaya DC, 5k to Trade Rite."

```
registry: GROUP RECEIPT GR-000231   R9 000   EFT   ref "MKHIZE 22/08"   posts NOTHING
 ├── leg → Hardware db    R1 000  → AR receipt SH-RC-000410 → INV-SH-000091      [Applied]
 ├── leg → DC db          R3 000  → AR receipt DC-RC-000122 → on account         [Applied]
 └── leg → Trade Rite db  R5 000  → AR receipt TR-RC-000377 → INV-TR-000188/190  [Pending]
```

- Each leg is an **idempotent command** keyed by `(group_receipt_id, allocation_id)`. Retry is always
  safe; recovery after an outage is to retry, never to re-key (ADR-104).
- A leg is `Pending` until its company acknowledges. Legs outstanding past their timeout raise an alarm
  and appear on the **unapplied-legs report** — the control that stops "in flight" from meaning "lost".
- An unallocated remainder sits on the registry container, in nobody's ledger, visible and ageing.
- **The bank is one company's.** That company debits bank and credits inter-company clearing; the
  benefiting company debits clearing and credits its customer. Two legs of one intent, each posting
  locally (ADR-105).
- **Net-zero is a standing reconciliation across databases**, run on a schedule and after every intent —
  not a transactional assertion, because there is no longer one to make. Clearing lines carry the intent
  id, so an unmatched leg is identifiable by document.
- **A period cannot be closed with an intent outstanding.** Stage 07c's close refuses and names it.

---

## 8. Financial reporting

- **Every statutory statement is produced inside one company's database.** Trial balance, income
  statement, balance sheet, VAT return, age analysis, cash book — each belongs to one company and
  reconciles on its own. There is no group VAT return.
- **Consolidated is a registry read model** assembled from each company's published period figures,
  eliminating inter-company clearing, watermarked *Consolidated — management information, not a
  statutory statement*, and carrying the `AsAt` of every contributing company. Where a company is stale,
  it says which — it never quietly sums old figures (ADR-106).
- **Period close is per company.** A group close is every member closed, and it refuses over an
  outstanding inter-company intent.

---

## 9. Operations — the parts that only exist because of this topology

| Concern | How it works |
|---|---|
| **Adding a company** | `Provisioning → Seeding → Registered → Active` (ADR-118). Create database, migrate, seed chart of accounts/numbering/tax/permissions, register the connection, activate. Re-runnable from the step that failed. Business operations see `Active` companies only. |
| **Migrations** | The runner fans out over every company database, registry first, recording `schema_version` per company. A company behind the binary is **not served** — it returns an actionable error naming the pending migration rather than executing against an unexpected schema (ADR-117). |
| **Backup / restore** | Per database, own schedule and retention, registry snapshotted most often. One company can be restored without stopping the others; afterwards the registry **re-drives** that company's intent legs, which is safe because legs are idempotent (ADR-120). |
| **Sync** | Three-tier sync runs per company database with its own outbox, inbox and cursors. The cloud mirrors the layout: a registry and N company databases. |
| **In-flight monitoring** | Outstanding intents, unapplied legs, unconfirmed credit holds and stale projections are four dashboards with named owners. They are not optional extras — they are how this topology is kept honest. |
| **Security** | The registry holds connection credentials: encrypted at rest, never in a support export, never in telemetry (R10, `docs/SECURITY.md`). |
| **Licensing** | One licence, one tenant, N companies. Company count is a metered, entitled dimension (Stage 04b). |

---

## 10. What this changes in existing modules

| Module | Change |
|---|---|
| Persistence (01) | `DbContext` resolution moves behind `ICompanyDbContextFactory`; the registry context is new |
| Master data (06) | Catalogue is per company; the routing index projects from it |
| Finance (07) | Already per database and therefore already separate; adds clearing accounts and the group containers' legs |
| Inventory (08) | Reservations and availability per company; the group availability projection publishes from here |
| POS (09) | The till belongs to one company, scans group-wide via the registry, and a sister-company line splits the sale into two documents |
| Sales (10 / 10c) | Pack size on invoice lines; one invoice per supplying company |
| Warehouse (13 / 13b) | A pick wave may span companies — the wave is registry-coordinated, each pick task local to its company |
| Order management (14) | Sourcing, splitting, COD, reservation on approval |
| Field sales (14b) | A rep sells for one or several companies; performance rolls up in the registry |
| Sync & backup (04) | Per-database cursors, per-database snapshots, intent re-drive after restore |

---

## 11. The rules a reviewer checks

`multi-company-guard` (`.claude/agents/multi-company-guard.md`) reviews every stage that touches this
document against exactly these:

1. **No command handler opens transactions against two databases.** No 2PC, no FDW, no wrapper that
   makes it look like one. Cross-database work is a saga with idempotent legs.
2. Every saga leg is idempotent, keyed by `(intent_id, leg_id)`, and safe to retry indefinitely.
3. Compensation is a new document — a release, a reversal, a credit note — never a delete or an edit.
4. No group read model is the basis for a commit. Reservations re-check in the owning company database;
   credit consumes in the registry under serialisation.
5. Every group figure crossing an API boundary carries `AsAt`, and a stale contributor is disclosed.
6. Credit holds expire, and exposure counts confirmed consumption plus unexpired holds.
7. Sourcing never produces a negative available quantity in any company, including when the planner is
   fed a stale projection.
8. A split document set reconciles to its source line for line and cent for cent, and every line is on
   exactly one document.
9. Number sequences are per company. A shared counter is a finding even if it currently looks unique.
10. Inter-company clearing nets to zero across databases, an outstanding intent blocks a period close,
    and every clearing line carries its intent id.
11. Consolidated output is labelled, read-only, and never a VAT return.
12. A fan-out read degrades — one company down returns that company as an error, not the whole call.
