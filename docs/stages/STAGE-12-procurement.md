# STAGE 12 — Procurement: requisitions, RFQs, purchase orders, goods receipts, three-way match and supplier scorecards

**Status:** DONE (2026-08-17) · **Depends on:** 08 (stock ledger — a receipt is a stock
movement), 11 (the import pipeline a supplier price list arrives through), and reads 06's partners
and 07's posting rules · **Reference reading:** `docs/DATA_MODEL.md` §1–§3, `docs/CONVENTIONS.md`
§1–§6, `docs/API_STANDARDS.md` §3–§5 and §8–§9, `docs/TESTING.md` §2–§3, `docs/IMPORT_PIPELINE.md`
§13, `CLAUDE.md` §7 rules 2, 3, 4, 6, 7, 8, 12 and 13, ADR-065 (document numbering), ADR-068
(weighted-average cost), ADR-070 (a missing posting rule does not refuse a movement), ADR-073 (a
refused stock movement does not fail the document), ADR-075 (tax per line), **ADR-081**–**ADR-086**
(the decisions this stage makes).

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 12-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-12-001 | Verify reopened procurement stage | Stages 08, 11 | NEEDS_VERIFICATION |

## Objective

Stage 09 and Stage 10 taught the system to sell. This one teaches it to buy, which is the other half
of the sentence `ROADMAP.md`'s "minimum viable trading system" ends on: *"Add **12** and it can also
buy."*

Buying is not one act, it is a chain of five documents that each exist because the previous one is
not yet trustworthy enough to spend money on:

- A **requisition** is somebody in the shop saying they need something. It commits nothing. Its
  entire purpose is to be the record that demand was internal and approved before a buyer went
  shopping — without it, "who asked for this?" has no answer.
- An **RFQ** is the buyer asking several suppliers what they would charge. Its responses are quotes,
  and a quote is a promise a supplier made on a date, so it is frozen once submitted. Awarding one
  is what turns a question into a commitment.
- A **purchase order** is the commitment. It is the document the supplier will hold the shop to, so
  once it is issued it does not change (§7 rule 7) — it is amended by a new version or cancelled.
- A **goods receipt note** is the claim that the goods physically arrived. It is the only document in
  the chain that moves stock, and it is what puts value into the books.
- The **three-way match** is the control the whole chain exists for: what was ordered, what arrived,
  and what the supplier is charging for, compared line by line before anybody pays.

And the **supplier scorecard** is the reason to keep the records at all — the difference between a
supplier who delivers on time in full and one who does not is invisible in any single delivery and
obvious over ninety days.

## What this stage does not own

- **No GL account, anywhere** (rule 12). Receiving stock raises Stage 08's valuation event, which the
  existing `inventory.receipt` posting rule turns into Dr Inventory / Cr GRNI. A passed match raises
  `procurement.invoice.matched`, which a seeded rule turns into Dr GRNI + Dr Input VAT / Cr Trade
  creditors. Procurement never names either side. **ADR-085.**
- **No AP invoice document.** The supplier's invoice *number*, date and amounts are recorded on the
  match so the comparison is auditable; the `ApInvoice` aggregate, its ageing and its payment stay
  Stage 07's. Procurement hands Finance a matched fact, not a posting instruction, and the two
  modules share no table.
- **No approval engine** (rule 13). A requisition and a purchase order both have an approval step and
  both call it through a documented no-op gate, exactly as Stage 07's journal and AP payment commands
  do, until Stage 05's `IApprovalService` exists. The state machine is real; the policy behind it is
  not this stage's to invent.
- **No supplier price-list fetch.** `PROGRESS.md` §5 and `IMPORT_PIPELINE.md` §13 both say Stage 12
  reuses Stage 11's pipeline rather than forking it, and that what it adds is a *fetch* against the
  existing `IImportSourceReader` seam. That is a scheduled-job concern with no procurement document
  behind it; it is left to the pipeline it belongs to and noted for whoever schedules it.
- **No new master data.** A supplier is a Stage 06 `Partner` with `PartnerType.Supplier`. An item is
  a Stage 06 item or variant. Procurement is a caller, the same way Stage 11 is (ADR-079).
- **No procurement screen.** Every deliverable is an endpoint, per R3, for the Windows/WPF reason
  Stages 09, 10 and 11 all stopped at (`PROGRESS.md` §4.3, ADR-031).
- **No landed cost, no consignment, no blanket orders, no MRP.** Replenishment suggestion is Stage 15
  (`ROADMAP.md`), bin-level putaway is Stage 13, and the supplier-facing side of the trading network
  is Stage 21b. This stage builds the documents those stages will drive.

## Deliverables

**`procurement` module** (schema `procurement`, thirteen tables)

*Demand*

- `PurchaseRequisition` — the aggregate root. `RequisitionNumber` from ADR-065's sequence (series
  `REQ`), requesting user, `RequiredBy` date, `Justification`, `Status`, and the who/when of approval
  and rejection. Owns its lines.
- `PurchaseRequisitionLine` — an item or variant, a quantity, an optional estimated unit cost, and
  the id of whatever downstream document consumed it, so a requisition can answer "did this ever get
  bought?".

*Sourcing*

- `Rfq` — `RfqNumber` (series `RFQ`), a title, a `ClosesAt`, `Status`, and the requisition it came
  from where it came from one. Owns its lines and its responses.
- `RfqLine` — what is being asked for: item or variant, quantity, and a free-text specification.
- `RfqResponse` — one supplier's quote. `PartnerId`, `Currency`, `QuotedAt`, `ValidUntil`, lead-time
  days, `Status`, and the who/when of the award. Frozen on submission.
- `RfqResponseLine` — that supplier's unit price and available quantity for one `RfqLine`.

*Commitment*

- `PurchaseOrder` — `OrderNumber` (series `PO`), `PartnerId`, `Currency`, delivery `LocationId`,
  `ExpectedAt`, `Status`, `Version`, the id of the order this one amends (where it is an amendment),
  the RFQ response it was awarded from, and the who/when of approval, issue, closure and
  cancellation. Carries `Net`, `Tax` and `Gross` as the sum of its lines.
- `PurchaseOrderLine` — item or variant, ordered `Quantity`, `UnitCost`, tax code and the per-line
  `Net`/`Tax`/`Gross` computed once at authoring time through `ITaxCalculator` (ADR-075), plus
  `ReceivedQuantity` and `InvoicedQuantity` as maintained running totals and the requisition line it
  satisfies.

*Fulfilment*

- `GoodsReceipt` — `ReceiptNumber` (series `GRN`), the purchase order, `LocationId`, `ReceivedAt`,
  the supplier's delivery-note number, `Status`, and the count of lines whose stock movement the
  ledger refused (ADR-073's reconciliation surface, mirrored from POS).
- `GoodsReceiptLine` — the PO line, the `ReceivedQuantity`, the `RejectedQuantity` and its reason,
  the unit cost the stock was valued at, the resulting `StockLedgerEntryId`, and the `StockPosting`
  status.

*Control*

- `SupplierInvoiceMatch` — the three-way match document. The purchase order, `PartnerId`, the
  supplier's own invoice number and date, the `Net`/`Tax`/`Gross` the supplier is claiming, the
  `Status` (`Matched`, `MatchedWithinTolerance`, `Blocked`), the tolerances that were applied, and
  the who/when of the release. **ADR-082.**
- `SupplierInvoiceMatchLine` — per PO line: ordered, received, invoiced and previously-invoiced
  quantities, the ordered and invoiced unit costs, the computed quantity and price variances, and the
  per-line verdict.

*Performance*

- `SupplierScorecard` — a frozen snapshot for one supplier over one closed period: orders placed,
  lines ordered, lines delivered on time, quantity ordered, quantity received, quantity rejected,
  the invoice-price variance total, and the four derived percentages. **ADR-084.**

**Application layer**

- `IPurchaseRequisitionRepository`, `IRfqRepository`, `IPurchaseOrderRepository`,
  `IGoodsReceiptRepository`, `ISupplierInvoiceMatchRepository`, `ISupplierScorecardRepository`.
- `IGoodsReceiptCompletionService` — the three steps a completion always takes (freeze, post stock,
  advance the order), in one place, the way `ISalesReturnCompletionService` is.
- `IThreeWayMatchEngine` — takes an order, its receipts, the supplier's claimed lines and a tolerance
  policy, and returns a match document. Pure, and tested as such.
- `ISupplierScorecardCalculator` — folds orders and receipts in a period into one snapshot.
- `IProcurementFinancialEventPublisher` — where a released match's financial fact goes; a logging
  default and a `IFinancialEventPoster`-backed real one, resolved at build time exactly as Stage 10's
  return publisher is.
- 21 commands, 9 queries, `ProcurementPermissions` (10 permissions), `ProcurementModuleManifest`
  (flag `procurement`, not core).

**API** — `/api/v1/procurement/...`, every operation in `/openapi/v1.json` with summaries, error
responses and permission requirements.

**Infrastructure** — thirteen EF configurations, six repositories, a reversible `Procurement`
migration, DI registration, and the seeded `procurement.invoice.matched` posting rule.

**Seed** — `scripts/seed.sh` produces a store that has really bought: an approved requisition, an RFQ
with two supplier responses, an awarded and issued purchase order, a goods receipt that moved stock
at cost, and a released three-way match with its journal.

## Business rules

1. **A requisition commits nothing.** It has no supplier, no price the shop is held to and no GL
   consequence. It may be approved or rejected; only an approved one may be converted.
2. **A quote is frozen on submission.** An `RfqResponse` and its lines cannot be edited once
   submitted — a supplier who wants to change their price submits a new response. Awarding is
   exclusive: one RFQ awards at most one response, and awarding closes the RFQ.
3. **A purchase order is immutable once issued** (§7 rule 7). Lines may be added, changed and removed
   in `Draft`; `Approved` and `Issued` freeze them. A change after issue is a new order at
   `Version + 1` that names the one it supersedes; the superseded order is cancelled.
4. **A purchase order's currency is the supplier's, bound at creation and never inferred per line.**
   Every line's cost, and every receipt and match against it, is in that currency. §4.13 is the
   lesson: a document that lets a currency in through a line permanently breaks the document.
5. **Tax is computed once, per line, at authoring time** (ADR-075). Nothing downstream recomputes an
   order's tax from its total — the match compares against the stored line figures.
6. **Nothing is received against anything but an issued order**, and never more than
   `Ordered − AlreadyReceived`, plus the tenant's over-receipt tolerance (default 0%). A delivery
   beyond tolerance is refused as a whole line; it is a conversation with the supplier, not a silent
   acceptance. **ADR-083.**
7. **Rejected quantity never enters stock.** A receipt line carries received and rejected separately;
   only the accepted quantity moves through the ledger, and only the accepted quantity counts as
   received against the order.
8. **Stock is valued at the order's unit cost**, not at today's average — that is what a purchase
   *is*, and it is the entry that makes the weighted average move (ADR-068).
9. **A stock movement the ledger refuses does not fail the receipt** (ADR-073). The goods are on the
   dock either way. The line records `StockPosting = Refused`, the receipt counts them, and
   `GET /procurement/reconciliation/stock-issues` lists them.
10. **A match is a document, not a boolean** (ADR-082). It is written whatever its verdict, including
    `Blocked`, because "why did we not pay this invoice" is a question somebody asks three weeks
    later.
11. **Nothing is invoiced that was not received.** Per line, `Invoiced + PreviouslyInvoiced` may not
    exceed `Received`. A supplier invoicing ahead of delivery blocks; it does not warn.
12. **A price variance within tolerance passes and is still recorded.** The default tolerance is 2%
    or R10 per line, whichever is greater, applied to the extended line. Beyond it, the line and the
    document both block.
13. **Only a `Matched` or `MatchedWithinTolerance` document may be released**, and releasing is what
    raises the financial event. A `Blocked` document is released by nobody; it is corrected by a
    credit note from the supplier and a new match.
14. **A released match is frozen** (§7 rule 7) and its `Invoiced` quantities are written back onto the
    order's lines, which is what makes rule 11 cumulative across several invoices for one order.
15. **A scorecard is a snapshot over a closed period, never a live figure** (ADR-084). Recomputing
    yesterday's supplier rating from today's data restates history, and a supplier rating that
    changes when nobody did anything is a rating nobody trusts. A period may be snapshotted once.
16. **On time means on or before `ExpectedAt`**, measured per receipt line against the order's
    expected date, because a delivery that is half on time is half on time.
17. **Everything is soft-deleted and tenant-scoped** (§7 rules 3 and 8), and no table in this schema
    draws a foreign key into another schema (`CONVENTIONS.md` §2).

## Tests / acceptance

**Unit** (Domain + Application, ≥ 80% line coverage on the stage's new code)

- The requisition state machine: approve, reject, refuse to convert a rejected one, refuse to
  approve twice.
- The RFQ: refuse a response after `ClosesAt`, refuse to edit a submitted response, refuse a second
  award, close on award.
- The purchase order: line arithmetic across several lines and a tax code; freeze on approve and on
  issue; amendment produces `Version + 1` and cancels its predecessor; a foreign-currency line is
  refused (rule 4, the §4.13 regression).
- Receiving: the over-receipt boundary exactly at, just inside and just beyond tolerance; rejected
  quantity excluded from both stock and the received total; cumulative receipts across two GRNs.
- The match engine, which is where most of the value is: exact match; quantity over-invoice; price
  within the percentage tolerance; price within the absolute floor but outside the percentage
  (the R10 rule, on a cheap line); price beyond both; invoicing for a line never received;
  cumulative invoicing across two invoices against one order; and the 0.005 rounding boundary on the
  extended price, which is §4.14's regression written for this module.
- The scorecard calculator: on-time and late receipts, a partial fill, a rejection, and a period with
  no orders producing a snapshot of zeroes rather than a null.

**Integration** (Testcontainers Postgres, real HTTP)

- The whole chain end to end: requisition → RFQ → two responses → award → PO → issue → GRN →
  stock really moved at cost → invoice matched → released → journal balances.
- A blocked match is written, is readable, and cannot be released.
- The migration applies and reverses (`Down` tested).
- Every endpoint appears in `/openapi/v1.json`.
- Permission enforcement on the four high-risk operations.

**Architecture**

- `LayeringTests` and `FinanceRulesTests` see the new code; no procurement type names a GL account.
- `ReplicationRulesTests` sees every new replicated entity.
- `CommandClassificationTests` sees every new command (the sweep is manifest-derived, ADR-078, so the
  module manifest registration is what puts them in scope).
- No cross-schema foreign key.

## Exit checklist

- [x] `dotnet build -c Release` — zero warnings, zero errors
- [x] `dotnet test` — all green (1,069: 668 unit, 33 architecture, 368 integration), 83.46% line
      coverage on the stage's Domain + Application, merged across the unit and integration runs
- [x] `Procurement` migration generated, applied and reversed (`MigrationTests` runs the `Down`)
- [x] Every endpoint in `/openapi/v1.json` with summaries and error responses
- [x] Permissions registered in the RBAC catalogue (`IModulePermissions`)
- [x] `procurement.invoice.matched` posting rule seeded; receipt posting reuses `inventory.receipt`
- [x] Module entitlement flag declared in the manifest
- [x] Usage counters — automatic, via the `procurement` schema (`UsageCounterSource` groups the audit
      trail by schema name, so the module is metered without a code change)
- [x] Replication scope declared on every new entity, `SYNC_AND_BACKUP.md` updated — the thirteen rows
      were added while closing this checklist, along with `DATA_MODEL.md`'s `4j` and its §5 registry
- [x] `scripts/seed.sh` produces a store that has really bought — run against a fresh database: an
      approved requisition, two quotes, `PO-000001`, `GRN-000001` moving 116 units at a **net** 16.0869,
      and `FF-INV-88213` matched and released. All seven journals balance
- [x] ADR-081 – ADR-086 appended to `docs/DECISIONS.md`
- [ ] **The six agent reviews run, findings acted on or recorded** — *not done*, as for Stages 10 and
      11; see `PROGRESS.md` §4.17. Everything above is self-reported by the sessions that wrote the code
- [x] `docs/PROGRESS.md` updated, committed

## Notes for later stages

- **Stage 13 (warehouse)** puts a bin under `GoodsReceiptLine`. The line already carries the location
  and the ledger entry; putaway is a second movement, not a change to this one.
- **Stage 15 (replenishment)** is the thing that will create requisitions automatically. It needs no
  new document — `PurchaseRequisition` is deliberately supplier-free so a forecast can raise one
  before anybody has chosen who to buy from.
- **Stage 18 (quality)** inspects what a GRN rejected. `RejectedQuantity` and its reason code are the
  hook; an inspection plan attaches to the receipt line, not to the order.
- **Stage 21b (Vuma Connect)** is the supplier's side of the same documents. A connected supplier's
  quote arrives as an `RfqResponse` *proposal* the retailer accepts (rule 18 in `CLAUDE.md` §7) — the
  aggregate is already shaped for it, because a response is authored by the supplier and frozen on
  submission either way.
- **The supplier price-list fetch** stays Stage 11's pipeline with a scheduled source reader, per
  `IMPORT_PIPELINE.md` §13. Do not fork it into this module.
