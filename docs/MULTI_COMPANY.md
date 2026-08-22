# MULTI-COMPANY — running several trading companies in one installation

> **The requirement, in the operator's words.** "We need to be able to run multiple companies
> simultaneously, and when invoicing, whichever stock is available on whatever database — without
> going into a negative — draw it from that one, and separate the invoices into two. So two companies
> can operate in one space: one sells hardware and one sells groceries. You scan and it picks up
> automatically which store it is."

This document is the contract for that. It is referenced by Stages **06c**, **07c**, **08c**, **10c**,
**13b** and **14b**, and by requirements **R11** and **R12** in `CLAUDE.md` §3.

---

## 1. The shape: one tenant, many companies, many stores

```
Tenant  (the customer of Vuma Retail — one licence, one subscription, one database)
 └── Company        e.g. "Siyaya Hardware"   "Siyaya DC"   "Trade Rite Groceries"
      ├── legal identity: registered name, company reg no, VAT no, own document numbering
      ├── own chart of accounts, own general ledger, own financial statements
      ├── own stock, own price lists, own AR and AP sub-ledgers
      └── Store / warehouse (physical premises, already `platform.stores`)
```

A **company** is a new level between tenant and store. It is what a customer or supplier contracts
with, what invoices are issued by, what a set of financial statements belongs to, and what owns stock.
A **store** stays what it always was: a physical place. One company may have many stores; a store
belongs to exactly one company.

### Why companies are logical, not separate databases

The operator says "whatever database". Physically separate databases per company would mean a
distributed transaction on every cross-company receipt, a fan-out query on every barcode scan, N
migration chains, N sync cursors and N backup sets — and would make the *group* features that are the
entire point (one credit limit across three companies, one receipt allocated across three companies)
either impossible or unreliable.

**Decision (ADR-099): companies are logical inside one tenant database.** Every business row already
carries `tenant_id`; it now also carries `company_id`, filtered globally the same way. To the operator
this is indistinguishable from separate databases — the books, the stock, the documents and the
numbering are all separate — while a cross-company question is one indexed query rather than three
round trips.

The seam is kept open: group-level reads go through `ICompanyDataSource`, so a later stage can back
one company with a physically separate database without any caller changing. Nothing in Stages 06c–14b
may bypass that interface to reach another company's tables directly.

---

## 2. Scoping rules

| Scope | Means | Used by |
|---|---|---|
| **Company-scoped** (the default) | The query sees exactly one company. `company_id` filter is applied by the DbContext, not by the caller. | Every module. All financial reporting. |
| **Group-scoped** | The query sees every company in the tenant the user is *entitled to see*. Must be requested explicitly and is auditable. | Barcode resolution, availability, credit exposure, receipting, rep dashboards, consolidated reporting. |

Rules that hold everywhere:

1. **A document belongs to exactly one company.** There is no such thing as a group invoice, a group
   journal or a group stock ledger entry. Group objects are *containers* that point at company
   documents (ADR-102, ADR-104).
2. **No general ledger entry ever crosses a company.** Value moving between companies posts to that
   company's inter-company clearing account, on both sides (ADR-105).
3. **Group scope is a permission, not a role.** `group.view`, `group.receipt`, `group.credit.manage`,
   `group.report`. A user with access to one company sees one company.
4. **A consolidated view is labelled as consolidated and posts nothing** (ADR-106).

---

## 3. Scanning across companies

The till, the handheld and the rep's tablet all scan the same way, and the scan says which company
owns the item.

```
scan "6001234567890"
   → group.barcode_index  (tenant_id, barcode) → company_id, item_id, variant_id, pack_size, uom
   → returns { company: "Trade Rite Groceries", item: "Sugar 2.5kg", packSize: 6, available: 143 }
```

- **One index, group-wide, covering `(tenant_id, barcode)`.** A scan is a single index probe. It does
  not fan out across companies, and it does not care how many companies exist (ADR-100).
- **The index is a projection**, rebuilt from `catalog.barcodes` by the same outbox that replicates
  them. It is never written by hand.
- **Collisions are resolved, never guessed.** If a barcode exists in more than one company:
  1. prefer the company of the terminal's active session,
  2. then the company that has available stock at the scanning location,
  3. if still ambiguous, return **all** candidates and make the operator choose. The API returns a
     `MultipleCompanyMatches` result — it never picks one silently.
- **Private label vs GS1.** In-house barcodes are namespaced per company on issue so the common case
  never collides in the first place.

---

## 4. Availability and sourcing: which company supplies the line

The rule the operator gave is the whole rule: **draw it from whichever company has it, and never draw
a company negative.**

```
Sales order line: 20 × hot plate
  ├─ Siyaya Hardware   available 12  → allocate 12
  ├─ Siyaya DC          available 30 → allocate 8
  └─ Trade Rite         available 0  → skip
Result: 2 allocations, 2 invoices, 0 backorder
```

- **Available ≠ on hand.** `available = on_hand − reserved − in_staging_not_yet_shipped`. Sourcing
  reads *available*, always (ADR-103, ADR-109).
- **Sourcing order** is configurable per tenant and defaults to: the ordering company first, then
  nearest store, then most available. Behind `ICompanySourcingStrategy` so a later stage can add cost
  or margin rules without touching callers.
- **A short line is a backorder, never a negative.** If group-wide availability cannot cover a line,
  what exists is allocated and the remainder becomes a backorder line on the order. No company is ever
  driven below zero, in any scenario, including concurrent orders — the reservation is taken inside the
  same transaction as the allocation (ADR-102, ADR-103).
- **Reservation, not deduction.** Allocation writes an append-only reservation, which reduces
  *available* immediately and leaves *on hand* alone until the goods actually ship. Stock is still the
  projection of an append-only ledger (`CLAUDE.md` §7 rule 6); nothing mutates a quantity column.

---

## 5. One order, several invoices

An order allocated across two companies produces **two invoices** — one per supplying company, each
with that company's letterhead, VAT number, document number sequence, GL and AR sub-ledger.

```
SO-2026-000412  (captured against Trade Rite, customer: Mkhize Spaza)
 ├── INV-TR-000188   Trade Rite Groceries   3 lines   R 4 210.00
 └── INV-SH-000094   Siyaya Hardware        1 line    R 1 899.00
        both carry GroupDocumentRef = SO-2026-000412
```

- The customer sees both invoices and one delivery. The picking, packing and delivery run consolidate
  across companies (Stage 13b); the *money* never does.
- Credit is checked once, against the group limit, before the split (§6).
- A credit note reverses within its own invoice's company. There is no cross-company credit note.
- COD applies per order and is inherited by every invoice the order produces (ADR-111).

---

## 6. Credit limits across companies

> "A user has three companies and each company has a client — that client must have a total credit
> available of 150k, that can be spread over all companies. Same with suppliers."

**A credit group holds the limit. Company accounts are members and consume it.**

```
Credit group: "Mkhize Spaza Group"     limit R150 000
 ├── Trade Rite      A/R account   exposure R 62 400   sub-limit R100 000
 ├── Siyaya Hardware A/R account   exposure R 31 000   sub-limit none
 └── Siyaya DC       A/R account   exposure R  9 800   sub-limit R 40 000
Group exposure R103 200 · group available R46 800
```

- **Available = group limit − Σ exposure across every member**, and a member may additionally carry
  its own sub-limit. A sale must pass **both** checks. A sub-limit can never raise the group ceiling.
- **Exposure** = posted balance + open orders reserved + undelivered COD-exempt documents, per the
  policy configured on the group. What counts is configuration; that it is the same definition in
  every company is not.
- **Concurrency is the hard part and is treated as such.** Two tills in two companies invoicing the
  same customer at the same instant must not both consume the last R5 000. The check and the
  consumption happen in one serialisable transaction against the group row (ADR-101). There is a
  concurrency test for exactly this, and it is not allowed to be a mock.
- **Suppliers are the same object with the sign reversed.** A supplier credit group caps what the
  whole tenant may owe one supplier across all its companies, so three companies cannot each quietly
  take R100 000 of stock on the same R150 000 facility.
- A customer or supplier may exist in one company only — a credit group of one member is normal and is
  not a special case in the code.

---

## 7. Receipting across companies

> "A customer has an account with three of the companies. Capture the total receipted, then allocate
> it — 1k to Siyaya, 3k to Siyaya DC, 5k to Trade Rite."

```
Group receipt GR-000231   R9 000   EFT   ref "MKHIZE 22/08"
 ├── R1 000 → Siyaya Hardware   → AR receipt SH-RC-000410 → allocated to INV-SH-000091
 ├── R3 000 → Siyaya DC         → AR receipt DC-RC-000122 → on account
 └── R5 000 → Trade Rite        → AR receipt TR-RC-000377 → allocated to INV-TR-000188, INV-TR-000190
```

- **The group receipt is a container, not a financial document.** It posts nothing. Each allocation
  creates a real `finance.ar_receipts` row in that company, which posts through that company's posting
  rules like any other receipt (ADR-104).
- **Unallocated money posts nowhere.** A group receipt may be captured with a remainder unallocated;
  that remainder is not in anybody's ledger until it is allocated. It is visible, it is reportable, and
  it ages.
- **The bank is one company's.** The cash landed in the bank account of one company. That company
  debits bank and credits inter-company clearing for every cent allocated to a sister company; the
  sister company debits inter-company clearing and credits its customer (ADR-105). Both sides post in
  the same transaction, and the clearing accounts must net to zero across the group — asserted by a
  test, and by a standing reconciliation report.
- The same shape serves supplier payments: one payment run, allocated across the companies that owe.

---

## 8. Financial reporting

- **Every statement is per company.** Trial balance, income statement, balance sheet, VAT return, age
  analysis, cash book — each belongs to one company and reconciles on its own.
- **Consolidated is a separate, labelled read model.** It sums the companies in a group, eliminates
  inter-company clearing balances, and is watermarked *Consolidated — management information, not a
  statutory statement.* It cannot be posted to, and it is not a tax document (ADR-106).
- **A company's books close on their own calendar.** Period close is per company; a group close is
  every member closed, not a separate concept.

---

## 9. What this changes in existing modules

| Module | Change |
|---|---|
| Master data (06) | `company_id` on items, price lists, partners; a partner may be shared across companies through a credit group |
| Finance (07) | Chart of accounts, numbering, periods and statements are per company; inter-company clearing accounts; group receipt container |
| Inventory (08) | Stock is owned by a company; availability is a group-wide read; reservations are new |
| POS (09) | The till belongs to a company but scans group-wide, and may sell a sister company's stock — which splits the sale into two documents |
| Sales (10 / 10c) | Price resolution is per company; invoices carry pack size; a document may be one of several from one order |
| Warehouse (13 / 13b) | A pick wave may consolidate across companies; each shipment confirmation stays company-scoped |
| Order management (14) | Sourcing, splitting, COD, reservation on approval |
| Field sales (14b) | A rep sells for one company or several; targets and performance roll up per company and per group |

---

## 10. The rules a reviewer checks

`multi-company-guard` (`.claude/agents/multi-company-guard.md`) reviews every stage that touches this
document against exactly these:

1. No query reaches another company's data except through `ICompanyDataSource` with group scope and a
   permission behind it.
2. No GL entry, no journal, no sub-ledger row crosses a company. Value between companies moves through
   inter-company clearing, both sides, same transaction.
3. No document number sequence is shared between companies.
4. Credit checks read the group and consume it under a serialisable transaction. A test proves two
   concurrent sales cannot both spend the last of a limit.
5. Sourcing never produces a negative available quantity in any company, under concurrency.
6. A split document set reconciles: the sum of the invoices equals the order, line for line and cent
   for cent, and every line is on exactly one invoice.
7. A group receipt's allocations sum to its captured amount, and the clearing accounts net to zero.
8. Consolidated output is labelled and read-only.
