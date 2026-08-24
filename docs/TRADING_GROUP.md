# TRADING GROUP — the Operator ID, company links, shared floors and mixed baskets

> **The requirement, in the operator's words.** "Siyaya Cash and Carry also has another company's stock,
> called Noortgats Hardware, mixed with their cash-and-carry items. Items share a floor but need to be
> separate when invoiced and receipted. A client orders items from Siyaya and Noortgats, or brings them
> together to the till, or places an order all together. It can be scanned together as it's going to the
> same client at the same time, but it must be invoiced and accounted for separately. So we need some
> sort of ID to say which companies belong together — like a user ID, where they pay per company, per
> user and per till, and they'll be interlinked. They can't be linked without this user ID, which
> determines which companies can be linked."

This document is the contract for that. It is referenced by Stages **04b**, **06c**, **06d**, **06e**,
**09b**, **07c**, **08c**, **10c**, **13b**, **14b**, **22b** and **30b**, by requirement **R13** in
`CLAUDE.md` §3, and by ADR-121 – ADR-128.

Read `docs/MULTI_COMPANY.md` first. That document says each company is its own database and nothing
crosses without a saga. **This document says which companies are even allowed to be in the same saga.**

---

## 1. The three identities, and why there are three

| Identity | What it is | Where it lives | Issued by |
|---|---|---|---|
| **Tenant** | The installation. One licence, one subscription, one set of databases. | Registry + every database's `tenant_id` | Vendor, at activation |
| **Operator ID** | *The operator's "user ID".* The person or business that owns companies. **The only thing that can link two companies.** | Signed into the licence, mirrored to `registry.operators` | Vendor control plane (Stage 30b), in the signed licence (Stage 04b) |
| **Company** | One legal trading entity. Own database, own books, own VAT number. | `registry.companies` + its own database | The operator, by provisioning (ADR-118) |

An Operator ID is not a login. A person logs in as a **user**; the Operator ID is the ownership identity
that a licence carries, and it is what the vendor bills against. One tenant may hold more than one
Operator ID — a bookkeeper running two unrelated clients' businesses on one server — and **companies
under different Operator IDs can never be linked**, no matter what anyone configures (ADR-121).

```
Licence (signed, monthly)
  └── Operator ID  OP-4K2X-9QN7
        ├── Company  Siyaya Cash and Carry     (own database)
        ├── Company  Noortgats Hardware        (own database)
        └── Company  Siyaya DC                 (own database)
              │
              └── COMPANY LINKS — mutually accepted, revocable, audited
                    Siyaya C&C ←→ Noortgats     (shared floor, shared till, shared basket)
                    Siyaya C&C ←→ Siyaya DC     (shared credit, no shared floor)
```

---

## 2. The company link

Sharing an Operator ID makes a link **possible**. It does not make one **exist**. A link is created
deliberately, accepted by both sides, scoped to what it permits, and revocable (ADR-122).

### What a link carries

```
CompanyLink
  operator_id            must be identical on both companies, or creation is refused
  company_a, company_b   the two companies, order-independent
  scopes                 the specific things this link permits — see the table
  status                 Proposed → Accepted → Active → Suspended → Revoked
  accepted_by_a/_b       user, timestamp, and the licence fingerprint at the time
  effective_from/_to
```

| Scope | Permits |
|---|---|
| `SharedFloor` | Both companies' stock held at one premises, on the same shelves and bins |
| `SharedTill` | One till may sell for both companies in one transaction — the mixed basket (§4) |
| `SharedCredit` | The two may share a credit group, so one limit spans them (`MULTI_COMPANY.md` §6) |
| `SharedReceipting` | One captured payment may be allocated across them (`MULTI_COMPANY.md` §7) |
| `SharedSourcing` | An order may be filled from either company's stock and split into two invoices |
| `SharedPicking` | Their orders may appear in one consolidated pick wave (Stage 13b) |
| `SharedReporting` | They may appear in one consolidated management view (never a statutory statement) |

A scope that is not granted is not merely hidden — the operation is **refused at the point of use**,
with a ProblemDetails naming the missing scope and the two companies.

### Where the link is checked

Not at configuration time. **Every time**, at the point of the operation (ADR-122):

| Operation | Required scope | Checked in |
|---|---|---|
| Add a sister company's item to a basket | `SharedTill` | Stage 09b basket line handler |
| Hold credit against a group limit | `SharedCredit` | Stage 06d `IGroupCreditService.TryHold` |
| Allocate a group receipt leg | `SharedReceipting` | Stage 07c allocation dispatch |
| Source an order line from a sister company | `SharedSourcing` | Stage 08c plan **and** commit |
| Put a sister company's line in a wave | `SharedPicking` | Stage 13b wave builder |
| Consolidated report over both | `SharedReporting` | Stage 07c consolidation |
| Hold a sister company's stock at a premises | `SharedFloor` | Stage 06e putaway/bin assignment |

**The check is `(operator_id match) AND (link Active) AND (scope granted) AND (both companies licensed
and current)**. A company whose licence has lapsed to read-only (ADR-028) drops out of every link for
writes while the others carry on — its stock cannot be sold by a sister till, and this fails clearly
rather than silently omitting it.

---

## 3. Shared floor — two companies, one set of shelves

A **premises** is a physical site. It is registry data because more than one company can occupy it.

```
registry.premises          "Siyaya Verulam"   address, geography, trading hours
   ├── store (Siyaya Cash and Carry)   → in Siyaya's database
   ├── store (Noortgats Hardware)      → in Noortgats' database
   └── bin/shelf layout                 mastered at the premises, mirrored into each company
```

- **The layout is mastered once, at the premises**, and mirrored into every occupying company's
  `warehouse` schema so Stage 13's bins, zones and pick tasks work unchanged. A bin code means the same
  physical shelf to both companies (ADR-124).
- **Stock in a bin still belongs to exactly one company**, in that company's own database. A shelf may
  hold both companies' goods; a *quantity* never spans them.
- **Physical count at a shared bin counts both companies' goods** and produces one count sheet per
  company from one walk. Variance posts in each company separately (Stage 13b).
- **A picker at a shared floor picks both companies' lines in one walk.** Pick tasks stay in their own
  company's database; the wave that groups them is registry-coordinated (Stage 13b).
- **Requires `SharedFloor`.** Without it, putting Noortgats stock in a Siyaya-occupied bin is refused.

---

## 4. The mixed basket — one customer, one till, two sets of books

This is the case the operator described, and it is Stage **09b**.

```
Customer at the till, one basket:
   2 × hot plate        scanned → routing index → Noortgats Hardware
   1 × 10kg maize       scanned → routing index → Siyaya Cash and Carry
   3 × work gloves      scanned → routing index → Noortgats Hardware

TRADING SESSION  TS-000914     (registry: customer, till, cashier, opened_at)
   ├── company segment: Noortgats     2 lines   R 1 899.00 incl VAT @ 15%
   └── company segment: Siyaya C&C    1 line    R   214.00 incl VAT @ 15%

Tender: R2 113.00 card, captured ONCE
   └── allocated: R1 899.00 → Noortgats, R214.00 → Siyaya C&C

Output:
   TAX INVOICE  NG-INV-004412   Noortgats Hardware, its VAT no, its GL, its AR
   TAX INVOICE  SC-INV-019338   Siyaya Cash and Carry, its VAT no, its GL, its AR
   BASKET SUMMARY TS-000914     both invoice numbers, one total — NOT a tax invoice
```

### The rules

1. **One scan flow.** The cashier scans the whole basket without thinking about companies; the routing
   index (ADR-100) decides which company each line belongs to. A line whose company is not linked with
   `SharedTill` is refused **at scan time**, with the reason, not at payment.
2. **Segments, not one document.** The trading session holds one **segment per company**. A segment is
   the future invoice: its own lines, its own discounts, its own tax computation, its own rounding.
3. **Tax is computed per segment, never on the basket.** Each company's VAT is its own, on its own
   invoice, under its own VAT number. Rounding happens per segment; the basket total is the sum of
   rounded segments, never the rounding of a sum.
4. **Tender is captured once and allocated across segments** through the same machinery as a group
   receipt (ADR-126) — a mixed-basket payment *is* a group receipt whose legs happen to be immediate.
   Allocation is proportional by segment total unless the cashier overrides it.
5. **Every segment becomes a real tax invoice in its own company's database.** Nothing about it is
   shared, and each is a complete, independent, legally valid document.
6. **The basket summary is not a fiscal document** and says so on its face. It exists so the customer
   holds one piece of paper listing both invoice numbers and one total. A customer may also be given
   both tax invoices, and by default is.
7. **The session completes atomically from the customer's point of view and as a saga underneath.**
   Every segment commits or the session does not complete; a segment that fails compensates the others
   (ADR-116). A half-completed basket is not a state the model can represent.
8. **Offline (R1) is preserved.** A mixed basket captured offline replays through the idempotent sync
   batch path; each segment lands in its own company's database on reconnect, and the session id is the
   idempotency key that stops a replay producing four invoices instead of two.
9. **Returns split by origin** (ADR-128). A customer returning two items from a mixed basket produces a
   credit note in each item's own company, against that item's own original invoice. There is no
   cross-company credit note, ever.
10. **Cash-up is per company.** One cashier, one drawer, one till session — and a cash-up that reports
    per company as well as in total, because the cash in the drawer belongs to two businesses. The
    drawer's physical total is reconciled once; the accountability is split.

---

## 5. Users, tills and what the vendor bills for

> "They can pay per company, per user and per till, and they will be interlinked."

### Users

- **A user is mastered in the registry**, once, with one login (ADR-127). Roles and permissions are
  **per company** — the same person may be a manager in Siyaya and a cashier in Noortgats, or have no
  access to Noortgats at all.
- The access token carries the companies the user may act in and the roles they hold in each. A request
  naming a company the token does not carry is a 403, not a filtered result.
- **A user may only be granted access to companies under the same Operator ID.** Granting across
  Operator IDs is refused by the registry.

### Tills

- A till (terminal) is registered to a **premises** and entitled for **one or more companies**. A till
  may sell for a sister company only where `SharedTill` is granted.
- Device registration, certificates and PIN login are unchanged (Stage 02); what is new is the company
  list on the terminal record.

### Billing and metering (Stage 04b entitlements, Stage 30b billing)

Three dimensions, exactly as the operator described (ADR-123):

| Dimension | Counted as | Entitlement |
|---|---|---|
| **Company** | Every `Active` company under the Operator ID | `MaxCompanies` |
| **Named user** | Every enabled user × every company they may act in | `MaxUsersPerCompany` |
| **Till** | Every registered terminal × every company it may sell for | `MaxTillsPerCompany` |

- **Linking does not create a discount and does not multiply a charge unexpectedly** — a user or till
  entitled for two companies counts once in each, and the operator sees it stated that way on the
  invoice rather than discovering it.
- **Metering stays counts-only** (R10). Company count, user count, till count, link count. No names, no
  turnover, no business data.
- **A lapsed company drops out of links for writes** and cannot be sold by a sister till. It does not
  take the other companies down, and the failure names the lapse.

---

## 6. Overview and separation — the two halves, and where each lives

The operator asked for both. They are not in tension; they are different artefacts with different rules.

| Question | Overview (registry, read-only, stamped) | Separation (company database, authoritative) |
|---|---|---|
| What's on the shelf? | Group stock lookup across linked companies, `AsAt` | Each company's own stock ledger and valuation |
| What does this customer owe? | Group exposure across linked companies | Each company's own AR age analysis and statement |
| What did we sell today? | Group sales dashboard by premises and by company | Each company's own sales journal and VAT |
| What did this rep do? | Group rep performance roll-up | Per-company rep figures |
| What's the basket worth? | Basket summary (not fiscal) | One tax invoice per company |
| What did the drawer take? | Till cash-up total | Per-company accountability within it |
| Financial statements | Consolidated management view, watermarked | Statutory statements, per company, always |

**The rule that keeps them straight:** an overview is a projection with an `AsAt` and it can be wrong by
seconds; a separated figure is authoritative and comes from the company's own database. Anything a
customer, an auditor or SARS relies on is the separated one (ADR-119, ADR-106).

---

## 7. What a reviewer checks

`multi-company-guard` reviews every stage that touches this document against exactly these:

1. **No cross-company operation of any kind happens without an Operator ID match, an `Active` link and
   the specific scope for that operation, checked at the point of use.** A check only at configuration
   time is a critical finding.
2. A link cannot be created, accepted or reactivated between companies under different Operator IDs —
   and the refusal is in the registry, not in a UI.
3. A lapsed or read-only company drops out of links for writes, with a named failure, without affecting
   the others.
4. Basket tax is computed per segment. A basket-level tax or rounding computation is a critical finding
   — it produces a wrong VAT figure on a real tax invoice.
5. Every segment produces a complete, independent tax invoice in its own company's database, with that
   company's number sequence and VAT number.
6. The basket summary is marked not-a-tax-invoice, everywhere it is rendered or emailed.
7. A basket completes as a saga: all segments or none, with compensation, and a replay of the session id
   produces the same invoices rather than new ones.
8. A mixed-basket return credits the original invoice's company.
9. A user or till entitled across companies is counted once per company in metering, and never carries a
   role in a company it was not granted.
10. Bin layout mirrored from a premises does not let a quantity span two companies.
