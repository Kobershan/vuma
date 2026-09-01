# STAGE 09b — The mixed basket: one till, one customer, two companies' books

**Status:** NOT_STARTED · **Depends on:** 09 (the till, and its four open defects must be fixed first — `PROGRESS.md` §4.11–§4.15), 06e (`SharedTill`), 06d (the saga coordinator and group receipt machinery), 07c (allocation and clearing), 08c (availability and reservations), 10c (the invoice document) · **Reference reading:** `docs/TRADING_GROUP.md` §4 in full, `docs/MULTI_COMPANY.md` §4–§5, `docs/DECISIONS.md` ADR-125, ADR-126, ADR-128, ADR-100, ADR-102, ADR-112, ADR-116, `docs/EXECUTION_STANDARD.md`, `docs/TESTING.md` §3

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 09b-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-09B-001 | Implement mixed-basket transaction and per-company invoices | Stage 09 verification; 06e, 07c, 08c, 10c | BLOCKED |

## Objective

A customer at Siyaya Cash and Carry puts two hot plates, a bag of maize and three pairs of gloves on the
counter. The hot plates and gloves are Noortgats Hardware's stock; the maize is Siyaya's. It is one
customer, one moment, one payment — and it must become **two tax invoices, two sets of books and two VAT
returns**, with the cashier never thinking about it.

This stage builds the trading session that makes that true, and it is the first place in the product
where a single human action writes to two databases.

## What this stage does not own

- **No link management.** `SharedTill` is Stage 06e's. This stage calls `RequireLink` and refuses.
- **No new payment types.** Tenders are Stage 09's. What is new is allocating one tender across segments.
- **No new invoice document.** Stage 10c's invoice is used unchanged, once per company.
- **No new credit note.** A mixed-basket return uses Stage 10's credit note, in the origin company.
- **No till UI.** `VumaRetail.Desktop` cannot build here (`PROGRESS.md` §4.3, ADR-031). Every deliverable
  is an endpoint plus the receipt/document layer. Mark the screen `UNVERIFIED — needs Windows`.

## Deliverables

### Registry domain — `src/VumaRetail.Domain/Registry/Trading/`

| Type | Notes |
|---|---|
| `TradingSession` | The basket. `PremisesId`, `TerminalId`, `CashierUserId`, optional `CustomerGroupPartnerId`, `OpenedAt`, `Status` (`Open, Tendered, Completing, Completed, Voided`), `IdempotencyKey`. |
| `TradingSessionSegment` | One per company. `CompanyId`, lines, its own totals, its own tax, its own rounding, `ResultingInvoiceId` (set on completion), `SegmentStatus`. |
| `TradingSessionLine` | Item, variant, quantity, uom, **pack size snapshot** (ADR-112), unit price, discount, tax code, resolved `CompanyId` from the routing index. |
| `TradingSessionTender` | Captured once against the session: type, amount, currency, reference. |
| `TenderAllocation` | Per segment. Defaults proportional by segment total; the cashier may override; must sum to the tender exactly. |

### Application — `src/VumaRetail.Application/Registry/Trading/`

| Type | Responsibility |
|---|---|
| `IMixedBasketService` | Open, add line, void line, price, tender, allocate, complete, void session. |
| `AddBasketLineCommandHandler` | Resolves the barcode (ADR-100) → company → **`RequireLink(sessionCompany, lineCompany, SharedTill)`** → prices in the owning company → appends to that company's segment. A refusal happens **here**, at scan time, never at payment. |
| `ISegmentPricer` | Prices a segment **entirely inside its own company's database**: its price list, its promotions, its tax rules, its rounding. |
| `ITenderAllocator` | Proportional by segment total, cent-exact, with the remainder cent assigned to the largest segment deterministically. Overridable by the cashier within the tender total. |
| `CompleteTradingSessionCommandHandler` | The saga: one leg per segment, each writing a complete sale + tax invoice + AR receipt in its own company's database. |
| `IBasketSummaryDocument` | The non-fiscal customer summary (§ "Documents" below). |

### Completion — the saga, in this exact order

```
1. Registry:  intent written, status Completing, idempotency key recorded    (ADR-116)
2. Per segment leg, inside that company's own database, one local transaction:
      a. reserve/consume stock through Stage 08c                              (ADR-103)
      b. write the pos.sale
      c. raise the financial event → that company's posting rules → its GL    (rule 12)
      d. write the tax invoice via Stage 10c                                  (ADR-125)
      e. write the finance.ar_receipt for that segment's tender allocation    (ADR-126)
3. All legs acknowledged → session Completed, invoice numbers returned
4. Any leg fails → compensate every completed leg (reversal documents, release
   rows — never deletes) → session returns to Tendered with the reason
```

### Documents — `src/VumaRetail.Reporting/Documents/`

| Document | Rules |
|---|---|
| **Tax invoice, per company** | Complete and independent: that company's legal name, registration number, **VAT number**, address, its own number sequence, its own VAT summary, pack sizes on lines (ADR-112). Legally valid on its own. |
| **Basket summary** | Lists both invoice numbers, the per-company subtotals and the one total. Carries, in a font no smaller than the body text: **"This is not a tax invoice. Your tax invoices are NG-INV-004412 and SC-INV-019338."** Never carries a VAT summary. |
| **Receipt (80mm)** | Prints both tax invoices in sequence, then the summary block. One customer-facing artefact per company plus one combined block — never one merged receipt with a single VAT line. |

### API — `src/VumaRetail.StoreServer/Endpoints/TradingSessionEndpoints.cs`

```
POST   /api/v1/trading-sessions                             open
POST   /api/v1/trading-sessions/{id}/lines                  add a scanned line
DELETE /api/v1/trading-sessions/{id}/lines/{lineId}         void a line
GET    /api/v1/trading-sessions/{id}                        segments, totals, per-company tax
POST   /api/v1/trading-sessions/{id}/tender                 capture the tender
PUT    /api/v1/trading-sessions/{id}/tender/allocations     override the allocation
POST   /api/v1/trading-sessions/{id}/complete               the saga; idempotent
POST   /api/v1/trading-sessions/{id}/void                   with a reason
GET    /api/v1/trading-sessions/{id}/documents              invoice ids + summary
```

### Permissions

`pos.basket.mixed` (add a sister company's line), `pos.basket.allocate.override`, `pos.basket.void`.

### Entitlement / metering

Gated behind the `MultiCompany` module flag **and** the `SharedTill` link scope. Counters: mixed sessions
per day, segments per session. Counts only.

## Business rules

1. A line's company comes from the routing index, never from the cashier (ADR-100).
2. **`RequireLink(..., SharedTill)` is called when the line is added.** A basket that could not be
   completed is never allowed to be built.
3. **Tax is computed per segment, never on the basket** (ADR-125). Each segment rounds its own lines and
   its own total. The basket total is the sum of rounded segment totals — never the rounding of a sum.
4. Discounts and promotions apply within a segment only. A promotion may not span companies, and one that
   would is not applied, with a reason on the line.
5. The tender allocation must equal the tender exactly, to the cent. The remainder cent goes to the
   largest segment, deterministically, and the rule is stated on the allocation response.
6. Completion is all segments or none (ADR-116). Compensation writes reversing documents; nothing is
   deleted or edited.
7. **Completion is idempotent on the session's idempotency key.** A replayed completion returns the same
   invoice numbers and creates nothing (`PROGRESS.md` §4.11's lesson, applied from the start).
8. Offline capture and replay go through `POST /api/v1/sync/batches` — no bespoke endpoint (R1, §4.11).
9. A mixed-basket return credits the origin invoice's company (ADR-128). There is no cross-company credit
   note, and attempting one is refused with both invoice numbers named.
10. Cash-up is per company **and** in total: one drawer, reconciled once, accountability split by
    segment.
11. A voided session releases every reservation it held and posts nothing.
12. A single-company basket takes **no** registry path and behaves exactly as a Stage 09 sale does today.
    Do not make the common case pay for the mixed one — there is a test asserting the registry is not
    touched.

## Parts — the build list

**A. Groundwork**
- [ ] A1 — Confirm Stage 09's §4.11–§4.15 defects are fixed. **If §4.11 is open, fix it first** — this
      stage doubles its blast radius
- [ ] A2 — Branch `stage-09b-mixed-basket` off `main`

**B. Domain**
- [ ] B1 — `TradingSession`, `TradingSessionSegment`, `TradingSessionLine`, enums, exceptions
- [ ] B2 — `TradingSessionTender`, `TenderAllocation`, the cent-exact allocation invariant
- [ ] B3 — Segment status machine and the session status machine

**C. Application**
- [ ] C1 — `AddBasketLineCommandHandler` with routing + `RequireLink` + per-company pricing
- [ ] C2 — `ISegmentPricer` against each company's own database
- [ ] C3 — `ITenderAllocator` including the remainder-cent rule
- [ ] C4 — `CompleteTradingSessionCommandHandler` as a saga with compensation
- [ ] C5 — Idempotent completion; replay returns the same invoice ids
- [ ] C6 — Void, line void, reservation release

**D. Infrastructure**
- [ ] D1 — Registry tables + migration
- [ ] D2 — Offline capture/replay through the sync batch path
- [ ] D3 — Cash-up extended with per-company accountability

**E. Documents**
- [ ] E1 — Per-company tax invoice (reuses 10c; assert the VAT number and sequence are the company's)
- [ ] E2 — Basket summary with the not-a-tax-invoice statement
- [ ] E3 — 80mm receipt: both invoices then the summary block

**F. API, tests, docs**
- [ ] F1 — `TradingSessionEndpoints.cs` + OpenAPI
- [ ] F2 — Permissions registered
- [ ] F3 — Seed: the operator's own example, end to end
- [ ] F4 — ADRs, `PROGRESS.md`, `docs/DATA_MODEL.md` §4l

## Tests / acceptance

Use these numbers. They are the operator's example and they are chosen so the rounding is real.

- `Mixed_basket_produces_one_tax_invoice_per_company` — 2 × hot plate @ R799.00 and 3 × gloves @ R100.33
  (Noortgats), 1 × 10kg maize @ R214.00 (Siyaya), VAT 15% inclusive. Two invoices, two VAT summaries,
  two number sequences, two databases. **Assert each invoice's VAT is computed on its own segment**, and
  that the two VAT amounts do not equal the VAT of the combined total.
- `Basket_total_is_the_sum_of_rounded_segments` — assert explicitly, with the failing alternative
  (rounding the sum) stated in the test as a comment so nobody "fixes" it back.
- `Tender_allocation_is_cent_exact_and_deterministic` — R2 113.99 across two segments; the remainder cent
  lands on the larger segment, every run.
- `Unlinked_company_line_is_refused_at_scan_time` — the refusal names both companies and `SharedTill`,
  and the basket has one segment, not two.
- `Completion_is_all_segments_or_none` — inject a failure in the second leg; the first company has a
  reversing document, no sale stands, and the session is back at `Tendered` with the reason.
- `Replayed_completion_returns_the_same_invoice_numbers` — call complete three times; two invoices exist.
- `Offline_mixed_basket_replays_once` — capture offline, replay the batch twice, two invoices.
- `Mixed_basket_return_credits_the_origin_company` — return one hot plate and the maize; two credit notes
  in two companies, each against its own original invoice.
- `Cross_company_credit_note_is_refused` — attempt to credit the maize against the Noortgats invoice;
  refused, both invoice numbers named.
- `Single_company_basket_does_not_touch_the_registry` — assert with a query counter on the registry
  context.
- `Voided_session_releases_every_reservation_and_posts_nothing`.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] Stage 09's §4.11 idempotency fix confirmed in place before this stage merged
- [ ] Migration reversible, `Down` **executed**
- [ ] `money-and-tax` run — **this stage's per-segment tax and rounding is exactly its brief**
- [ ] `multi-company-guard`, `stock-availability-guard`, `sync-and-offline`, `architecture-guard` run
- [ ] Seed produces the operator's example and the two invoices are quoted in `PROGRESS.md`
- [ ] The till screen marked `UNVERIFIED — needs Windows`, with what must be re-run stated
