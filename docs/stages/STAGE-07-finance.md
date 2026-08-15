# STAGE 07 — Finance: GL, AR, AP, banking, tax, posting rules engine

**Status:** DONE (merged to `main`, 2026-08-15) · **Depends on:** 06 (built and merged) · **Reference
reading:** `docs/DECISIONS.md` ADR-016 (the spec this stage implements), `docs/CONVENTIONS.md`,
`docs/DATA_MODEL.md`, `docs/API_STANDARDS.md`, `docs/SYNC_AND_BACKUP.md`, `docs/TESTING.md`,
`docs/stages/STAGE-04b-licensing.md` (the template this document follows)

## Scope note — written before Stage 06 landed

> **Update, 2026-08-15.** Stage 06 has since been built and merged, and Stage 07 is now merged too.
> Nothing below changed as a result: Finance still references partners by bare `PartnerId` and still
> has no approval gate. Resolving partner names is a read model Stage 06's surface adds, and the
> approval gates are Stage 05's to configure against the same commands. The note is kept as written
> because it explains why the boundaries are where they are.

Two other sessions are building Stage 05 (workflow/approvals) and Stage 06 (master data:
items, variants, UoM, partners) concurrently, each in its own worktree. Finance genuinely needs
to reference a counterparty (AR needs a customer, AP needs a supplier) and, eventually, an
approval gate on large postings and payment releases. Neither dependency is built yet, so this
stage references both by a **bare opaque ID** rather than building any part of another stage's
surface:

- `PartnerId` (`src/VumaRetail.Domain/Finance/PartnerId.cs`) is a `Guid` wrapper and nothing
  else — no lookup, no name, no master-data shape. AR and AP invoices carry a `PartnerId` and
  Finance never resolves it to a name; that is Stage 06's job when it lands, and it lands by
  Stage 06 adding a read model, not by Finance changing shape.
- No `IApprovalService` is implemented or stubbed. Large-journal posting and AP payment release
  are documented as deferred approval gates in `docs/PROGRESS.md` §3 and wired with a no-op today
  (every posting and every payment succeeds without a gate) so Stage 07 does not block on Stage 05
  landing, and Stage 05 configures a real policy against the same command later without a
  Finance-side code change (the command shape does not change — only whether the pipeline gates it).

## Objective

The double-entry financial core ADR-016 calls for: a chart of accounts with analysis dimensions,
accounting periods that block close on unreconciled sub-ledger variance, an immutable
general ledger, a posting rules engine that is the *only* place any module may cause a GL
account to be named, AR and AP sub-ledgers reconciling to GL control accounts, a minimal real
banking slice, and a tax rules engine (never a constant). Built ahead of Inventory (08) and POS
(09) because both post to it.

## Deliverables

**Chart of accounts** (`finance.accounts`)
- `Account` — code, name, `AccountType` (Asset/Liability/Equity/Revenue/Expense), optional parent
  for a hierarchy, `ControlAccountType` (None/AccountsReceivable/AccountsPayable/Bank) marking the
  accounts AR/AP/banking reconcile to, currency, active flag.
- Analysis dimensions on every journal line: store (the base `Entity.StoreId` column — no second
  column needed), plus bare-opaque-ID `DepartmentId`, `CostCentreId`, `ProjectId`, `ChannelId`,
  `EmployeeId` — all nullable `Guid`, none resolved to a name by this module.

**Accounting periods** (`finance.accounting_periods`)
- `AccountingPeriod` — `PeriodStart`/`PeriodEnd`, `Status` (Open/Closed), closed-by/closed-at.
- `IPeriodVarianceChecker` compares each control account's GL balance against its sub-ledger
  (open AR invoice balances, open AP invoice balances, bank account reconciled balance) and
  reports a variance. `ClosePeriodCommandHandler` refuses to close on any nonzero variance.
- `FinanceReconciliationHostedService` — the "automated daily job" ADR-016 calls for — runs the
  same checker across every open period once a day and writes a `ReconciliationVarianceFlag` row
  so a variance is visible *before* somebody tries to close, not only at the close attempt. See
  ADR-063 for why this is a plain `IHostedService` rather than a Quartz.NET job.

**General ledger** (`finance.journals`, `finance.journal_lines`)
- `Journal`/`JournalLine` — double-entry, `IImmutableRecord` once `Status = Posted`. Rejected
  unless debits equal credits per currency. Amended only by `ReverseJournalCommand`, which posts
  an equal-and-opposite journal and links back with `ReversalOfJournalId` — never edits the
  original (CLAUDE.md §7 rule 7).
- `PostManualJournalCommand` for adjustments an accountant enters directly.

**Posting rules engine** — the mechanism CLAUDE.md §7 rule 12 names
- `IFinancialEvent` (`src/VumaRetail.Application/Abstractions/Finance/FinancePorts.cs`) — the
  contract a producer module raises: event type, tenant/store, dimensions, a named-amount
  dictionary (`"Net"`, `"Tax"`, `"Gross"`, …), source reference. **No field on this contract can
  name a GL account** — that is what makes rule 12 true by construction rather than by review.
- `PostingRule`/`PostingRuleLine` (`finance.posting_rules`) — tenant-configured data mapping one
  event type to an ordered list of (account, debit-or-credit side, which named amount, whether the
  account inherits the event's dimensions). Not code; a rule for a new event type is a data row Stage
  09/12/etc. seed at their own stage.
  `PostingRuleEngine` resolves a rule for an incoming event and posts a balanced journal.
- **Architecture test** (`FinanceRulesTests.No_module_outside_finance_references_a_GL_account`):
  no assembly other than `VumaRetail.Domain` (Finance namespace), `VumaRetail.Finance`,
  `VumaRetail.Infrastructure` and `VumaRetail.Web` may hold a type dependency on
  `VumaRetail.Domain.Finance.Account`. Proved by a deliberate violation (recorded in the test's own
  remarks) before being committed, per every prior stage's architecture rule.

**AR** (`finance.ar_invoices`, `finance.ar_receipts`)
- `ArInvoice`/`ArInvoiceLine` — draft, then posted (immutable, posts to GL through the posting
  engine using event type `ar.invoice.posted`).
- `ArReceipt` + allocations against one or more invoices; posts `ar.receipt.posted`.
- `GetArAgeingQuery` — current/30/60/90/90+ buckets per partner, as of a given date.

**AP** (`finance.ap_invoices`, `finance.ap_payments`) — the mirror of AR, event types
`ap.invoice.posted` / `ap.payment.posted`, `GetApAgeingQuery`.

**Banking** (`finance.bank_accounts`, `finance.bank_statement_lines`) — a minimal, real,
testable slice, not gold-plated:
- `BankAccount` — one per GL bank control account.
- `BankStatementLine` — imported (a batch command, not a file-parser; Excel/PDF ingest is Stage
  11's job), with a nullable `MatchedJournalLineId`. `MatchBankStatementLineCommand` /
  `UnmatchBankStatementLineCommand` are the reconciliation primitive; a statement line's matched
  state *is* the reconciliation, so there is no separate reconciliation-run entity to keep in sync.

**Tax** (`finance.tax_rules`) — a rules engine, not a constant (CLAUDE.md §9)
- `TaxRule` — code, rate, inclusive/exclusive, effective-from/to, active flag. Tenant-configurable;
  seeded with the `en-ZA`/15%-inclusive default and nothing else, so a second jurisdiction is a data
  row, not a code change.
- `TaxEngine.Calculate` — net/tax/gross for a code as of a date, honouring inclusive/exclusive.

## Business rules

- A journal is rejected, not silently balanced, if debits ≠ credits in any currency it carries.
- A posted journal, a posted AR/AP invoice and a receipt/payment are `IImmutableRecord` — no
  update, no delete, ever. Correction is a reversal or a credit/debit note.
- No module outside this one constructs an `Account` reference or a `Guid` it treats as one — every
  other module speaks `IFinancialEvent` and gets a journal back, never an account id.
- A period cannot close while any control account disagrees with its sub-ledger by a nonzero amount.
- Tax is calculated from `TaxRule` data as of the transaction date; nothing in code assumes 15%
  or VAT specifically.
- Money on every posted amount is `decimal(18,4)` with an explicit ISO currency (rule 4); a journal
  mixing currencies is two balanced sub-journals, not one journal with a currency column that lies.

## Tests / acceptance

- A journal with unequal debits and credits is rejected before it reaches the database.
- Posting, then attempting to edit or delete a posted journal/invoice/receipt/payment, is refused.
- Reversal produces an equal-and-opposite journal, links back, and leaves the original untouched.
- Posting rules engine: two realistic example event types (`ar.invoice.posted`,
  `pos.sale.tendered` — a stand-in for the Stage 09 event that does not exist yet) each resolve to
  a balanced journal from tenant-configured rule data, and an event with no matching rule is
  refused with a stable error rather than silently dropped.
- AR/AP ageing buckets a set of invoices correctly as of a fixed clock date.
- Bank statement line match/unmatch changes the reconciled balance the period-close check reads.
- Period close is refused when a control account and its sub-ledger disagree, and succeeds once
  they agree.
- Tax engine: inclusive and exclusive calculation for the seeded 15% rule, and a second rule with a
  different rate and effective date proves it is data, not a constant.
- Architecture test: nothing outside Finance's own assemblies references `Account` directly.
- EF migration: up → down → up on an empty database.

## Exit checklist

> **Re-walked and re-ticked 2026-08-15.** Every box below was already ticked when this document
> arrived on the branch — including the zero-warning build and the reversible migration, at a point
> when no migration existed and the EF model could not be constructed. The ticks below are the ones
> that were verified: build and test output, a migration applied and reversed against real
> PostgreSQL, and the module driven over HTTP.

- [x] Chart of accounts with analysis dimensions
- [x] Accounting periods with a variance-blocked close and a daily variance check
- [x] Immutable double-entry GL, balanced-or-rejected, amended only by reversal
- [x] Posting rules engine with `IFinancialEvent` contract and the rule-12 architecture test —
      proved by a deliberate violation, whose blind spot is recorded in `FinanceRulesTests`' remarks
- [x] AR and AP sub-ledgers referencing partners by bare `PartnerId`, reconciling to GL
- [x] Minimal real banking slice: accounts, statement lines, match/unmatch
- [x] Tax as a configurable rules engine, seeded with the `en-ZA` default only
- [x] Zero-warning build (`dotnet build -c Release`, 0 warnings 0 errors), 674 tests green
      (377 unit, 31 architecture, 266 integration), migration reversible (up → down → up asserted
      against real PostgreSQL in `FinancePersistenceTests`), RBAC permissions and module manifest
      registered *and reachable* (`AddVumaFinance` now scans its own assembly for handlers — it did
      not, and every endpoint would have 500'd), entitlement flag wired, seed data extended and
      verified idempotent, `docs/PROGRESS.md` + ADR-063…067 written
- [ ] Stage 05/06 integration (approval gates, resolved partner names) — deferred, documented in
      `docs/PROGRESS.md` §5, not blocking this stage's completion
