# STAGE 07c — Cross-company money: group receipting, allocation, inter-company clearing, consolidated reporting

**Status:** NOT_STARTED · **Depends on:** 07, 06c, 06d · **Reference reading:** `docs/MULTI_COMPANY.md` §2, §7, §8, `docs/DECISIONS.md` ADR-104, ADR-105, ADR-106, ADR-116, ADR-016, `CLAUDE.md` §7 rules 7, 12

## Objective

Money arrives once and belongs to several companies — and those companies are separate databases
(ADR-099), so nothing here can be one transaction. This stage builds the registry container that
captures the money, the saga legs that turn allocations into real per-company receipts, the paired
clearing intents that keep every company's books balanced on their own, and the consolidated read model
that shows the group without ever posting to it.

## Deliverables

**Registry**
- `GroupReceipt` — captured amount, currency, tender, reference, receiving bank account (which belongs
  to one company), capture date, status (`Draft`, `PartiallyAllocated`, `Allocated`, `Reversed`).
  Immutable once fully allocated; a correction is a reversal (`CLAUDE.md` §7 rule 7).
- `GroupReceiptAllocation` — company, customer account, optional target invoices, amount, and its leg
  state (`Pending` → `Applied` | `Compensated`). Σ allocations ≤ captured amount, as a check constraint
  and in the aggregate. Each allocation is dispatched through 06d's `ISagaCoordinator` as an idempotent
  leg keyed by `(group_receipt_id, allocation_id)` (ADR-104, ADR-116).
- `GroupPaymentRun` / `GroupPaymentAllocation` — the same shape for money going out to a supplier that
  several companies owe.
- `InterCompanyClearingIntent` — immutable, carrying both legs and the group document that caused them.
  Settled only when both companies acknowledge (ADR-105).

**Application**
- `IGroupReceiptService` — capture, allocate, part-allocate, reverse. Allocation dispatches legs; it
  does not post. A retried call cannot double-post, and recovery from a company outage is retry, never
  re-key.
- **Per-company leg handler** — runs *inside* the target company's database: creates
  `finance.ar_receipts`, raises the financial event, posts through **that company's** posting rules. No
  module names a GL account (§7 rule 12).
- `IConsolidationService` — trial balance, income statement, balance sheet and age analysis assembled in
  the registry from each company's published period figures, inter-company clearing eliminated, marked
  `IsConsolidated`, carrying each contributor's `AsAt` and naming any that are stale (ADR-106, ADR-119).
- **Period close refuses over an outstanding inter-company intent**, and names it.

**Infrastructure / posting**
- New posting-rule keys: `GroupReceiptAllocated`, `InterCompanyClearingRaised`,
  `InterCompanySettled`, seeded with a default rule set.
- Inter-company clearing account per company pair direction, auto-created on first use from a template
  in the chart of accounts.
- `finance.ar_receipts` and `finance.ap_payments` gain `group_document_id` and `intent_id` (both
  nullable) — a receipt captured directly in one company is still a normal receipt with nulls there.
- Clearing lines carry the intent id, so an unmatched leg is identifiable by document rather than by
  guesswork.
- **The net-zero reconciliation is a scheduled job across databases**, run after every intent and on a
  schedule, with an alarm and a named owner — not a transactional assertion, because there is no longer
  one to make.

**API** — `/api/v1/group-receipts` (capture, allocate, reverse, list unallocated),
`/api/v1/group-payments`, `/api/v1/reports/consolidated/*`. Every consolidated response carries the
watermark field.

**Permissions** — `group.receipt.capture/allocate/reverse`, `group.report.consolidated`.

## Business rules

1. **A group receipt posts nothing.** Only a leg posts, and it posts inside one company's database.
2. Unallocated money sits on the group receipt, in nobody's ledger, and ages on an unallocated report.
3. The bank side posts in the company that owns the receiving bank account. Every cent allocated to a
   sister company creates a clearing pair, both legs in the same transaction (ADR-105).
4. Clearing balances across a group net to zero at every instant. There is a standing reconciliation
   report and a test that asserts it after a randomised sequence of allocations and reversals.
5. Allocating to an invoice cannot over-allocate it, across companies or within one.
6. A reversal reverses the **intent**, which dispatches reversing legs to every company involved. It
   never edits a posted journal and never reverses one leg on its own.
7. Consolidated output is labelled, read-only, and is not a statutory or tax document (ADR-106).
8. VAT is company-level, always. There is no group VAT return.

## Tests / acceptance

- The operator's example, end to end across three real databases: R9 000 captured, allocated R1 000 /
  R3 000 / R5 000, two against specific invoices, one on account. Three AR receipts exist in three
  databases, all three trial balances still balance, clearing nets to zero, and group exposure drops by
  exactly R9 000.
- **One company's database is stopped mid-allocation**: its leg stays `Pending`, the other two apply,
  the unapplied-legs report shows it with an age, and bringing the database back applies it exactly once
  with no operator action beyond a retry.
- A period close attempted with that leg outstanding is refused and names the intent.
- Capture R9 000, allocate R4 000, leave R5 000: nothing of the R5 000 appears in any company's ledger,
  and the unallocated report shows it with an age.
- Reverse a fully allocated group receipt: every leg reverses, no journal is edited, exposure returns.
- A retried allocation call with the same allocation id posts once; a leg that fails three times then
  succeeds applies once.
- Randomised property test: 200 allocations, reversals and injected leg failures across 3 databases —
  clearing nets to zero once every intent settles, and every unsettled intent is on the report.
- Consolidated income statement over three companies with inter-company trade eliminated equals the
  hand-computed figure in the test fixture.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `money-and-tax` and `multi-company-guard` run, findings closed
- [ ] Migration reversible, `Down` executed
- [ ] Posting rules registered and seeded; no GL account named outside the rules engine
- [ ] `docs/DATA_MODEL.md` §4f and §4l extended; replication registry updated
- [ ] The net-zero reconciliation job, the unapplied-legs report and their alarms exist and are seeded
- [ ] Seed: the three-company group with one shared customer, one group receipt allocated across all three
