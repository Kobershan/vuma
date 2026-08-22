# STAGE 07c — Cross-company money: group receipting, allocation, inter-company clearing, consolidated reporting

**Status:** NOT_STARTED · **Depends on:** 07, 06c · **Reference reading:** `docs/MULTI_COMPANY.md` §2, §7, §8, `docs/DECISIONS.md` ADR-104, ADR-105, ADR-106, ADR-016, `CLAUDE.md` §7 rules 7, 12

## Objective

Money arrives once and belongs to several companies. This stage builds the container that captures it,
the allocation that splits it into real per-company receipts, the clearing entries that keep every
company's books balanced on their own, and the consolidated read model that shows the group without
ever posting to it.

## Deliverables

**Domain**
- `GroupReceipt` — captured amount, currency, tender, reference, receiving bank account (which belongs
  to one company), capture date, status (`Draft`, `PartiallyAllocated`, `Allocated`, `Reversed`).
  Immutable once fully allocated; a correction is a reversal (`CLAUDE.md` §7 rule 7).
- `GroupReceiptAllocation` — company, customer account, optional target invoices, amount. The sum of
  allocations may never exceed the captured amount.
- `GroupPaymentRun` / `GroupPaymentAllocation` — the same shape for money going out to a supplier that
  several companies owe.
- `InterCompanyClearingEntry` — the paired both-sides record, with the group document that caused it.

**Application**
- `IGroupReceiptService` — capture, allocate, part-allocate, reverse. Allocation is idempotent by
  `(groupReceiptId, allocationId)` so a retried call cannot double-post.
- Posting: each allocation raises a financial event in **its own** company; Stage 07's posting-rules
  engine decides the accounts. No module names a GL account (§7 rule 12).
- `IConsolidationService` — trial balance, income statement, balance sheet and age analysis over a
  company group, with inter-company clearing eliminated and the result marked `IsConsolidated`.

**Infrastructure / posting**
- New posting-rule keys: `GroupReceiptAllocated`, `InterCompanyClearingRaised`,
  `InterCompanySettled`, seeded with a default rule set.
- Inter-company clearing account per company pair direction, auto-created on first use from a template
  in the chart of accounts.
- `finance.ar_receipts` and `finance.ap_payments` gain `group_document_id` (nullable) — a receipt
  captured directly in one company is still a normal receipt with a null group reference.

**API** — `/api/v1/group-receipts` (capture, allocate, reverse, list unallocated),
`/api/v1/group-payments`, `/api/v1/reports/consolidated/*`. Every consolidated response carries the
watermark field.

**Permissions** — `group.receipt.capture/allocate/reverse`, `group.report.consolidated`.

## Business rules

1. **A group receipt posts nothing.** Only an allocation posts, and it posts in one company.
2. Unallocated money sits on the group receipt, in nobody's ledger, and ages on an unallocated report.
3. The bank side posts in the company that owns the receiving bank account. Every cent allocated to a
   sister company creates a clearing pair, both legs in the same transaction (ADR-105).
4. Clearing balances across a group net to zero at every instant. There is a standing reconciliation
   report and a test that asserts it after a randomised sequence of allocations and reversals.
5. Allocating to an invoice cannot over-allocate it, across companies or within one.
6. A reversal reverses every leg it created, including the clearing pairs, and never edits a posted
   journal.
7. Consolidated output is labelled, read-only, and is not a statutory or tax document (ADR-106).
8. VAT is company-level, always. There is no group VAT return.

## Tests / acceptance

- The operator's example, end to end: R9 000 captured, allocated R1 000 / R3 000 / R5 000 across three
  companies, two of the allocations against specific invoices, one on account. Three AR receipts exist,
  three companies' trial balances still balance, clearing nets to zero, and the customer's group
  exposure drops by exactly R9 000.
- Capture R9 000, allocate R4 000, leave R5 000: nothing of the R5 000 appears in any company's ledger,
  and the unallocated report shows it with an age.
- Reverse a fully allocated group receipt: every leg reverses, no journal is edited, exposure returns.
- A retried allocation call with the same allocation id posts once.
- Randomised property test: 200 allocations and reversals across 3 companies, clearing nets to zero.
- Consolidated income statement over three companies with inter-company trade eliminated equals the
  hand-computed figure in the test fixture.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `money-and-tax` and `multi-company-guard` run, findings closed
- [ ] Migration reversible, `Down` executed
- [ ] Posting rules registered and seeded; no GL account named outside the rules engine
- [ ] `docs/DATA_MODEL.md` §4f extended; replication registry updated
- [ ] Seed: the three-company group with one shared customer, one group receipt allocated across all three
