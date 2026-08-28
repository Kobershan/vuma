# STAGE 10b — Customer Accounts, Lay-by & Stokvels

**Status:** NOT_STARTED · **Depends on:** 07, 09, 10 · **Reference reading:** `docs/DATA_MODEL.md` (savings), `docs/DECISIONS.md` ADR-055

## Task index

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-10B-001 | Implement customer credit accounts and lay-by | Stages 07, 09, 10 | NOT_STARTED |
| TASK-10B-002 | Implement stokvels and complete Stage 10b verification | TASK-10B-001 | NOT_STARTED |

## Objective
The three ways customers pay over time rather than all at once. Each has completely different accounting
and completely different failure modes, and getting any of them wrong loses real money — so they get
their own module rather than being bolted onto sales.

## Deliverables

### Customer credit accounts
- Account opening with credit application, approval workflow (Stage 05), credit limit and terms
- Running account with the AR sub-ledger from Stage 07: invoice, payment, allocation, ageing
  (current/30/60/90/120+), statements on a cycle, interest on overdue, settlement discount
- Credit control: limit checked **at tender time** including unsynced offline account sales, hold and
  release with approval, dunning through the notification service, write-off with approval
- Account cards and authorised buyers: a business account where several named people may charge to it,
  each with their own limit and their own trace on every transaction
- Statement delivery by email, SMS link and print; payment by EFT, card, cash at till, or the storefront

### Lay-by (layaway)
- Agreement: deposit, instalment schedule, term, expiry date, cancellation terms — printed and signed
- **Stock is reserved, not sold.** No revenue, no stock issue, no cost of sale until the final payment.
  The goods sit in a lay-by location, allocated to that agreement, and cannot be sold to anyone else.
- Instalments taken at any store, receipted, with running balance and remaining term on the receipt
- Completion: final payment converts the agreement to a normal sale — one stock issue, one revenue
  recognition, dated at completion, with the deposit and instalments allocated against it
- Cancellation and forfeiture per tenant policy (refund less an administration fee, or forfeit per the
  agreement), with the correct postings and the stock released back to sellable
- Expiry handling with escalating reminders before the date, not after
- Price protection: the agreed price holds for the term even if the shelf price moves

### Stokvels (group savings and buying schemes)
A stokvel is a group that contributes regularly and draws down together, usually into goods. Retailers
run them at scale, they are trust-heavy, and members will query a single rand. Model them properly.

- **Group** — name, type (savings, grocery/hamper, burial, investment-buying), constitution, cycle
  (usually annual, paying out before December), store, status
- **Members** — each with their own contribution obligation and **their own balance**; roles for
  chairperson, treasurer and secretary with different permissions; joining and leaving mid-cycle with a
  pro-rata rule
- **Contributions** — scheduled amounts, taken at till, by EFT, by debit order or on the storefront;
  receipted to the **individual member**, not just the group; arrears tracking per member with reminders
- **Group ledger** — append-only, like every other ledger in Vuma. Group balance is a projection of
  member balances. There is no "set balance" operation.
- **Benefits** — tenant-configured bonus or interest on the pooled balance, allocated to members
  proportionally to what each contributed *and when* (time-weighted, not a flat split — this is where
  disputes come from), plus negotiated group discounts on the payout basket
- **Payout / draw-down** — convert a member's balance into goods (the common case), a hamper against a
  predefined basket, cash, or a store credit. Payout runs as a normal sale against the member's balance,
  so stock, margin and tax all behave correctly and the goods are traceable.
- **Hamper baskets** — predefined baskets at a group price, stock reserved ahead of the December rush,
  substitution rules when an item is unavailable
- **Statements** — per member and per group, on demand and on schedule, showing every contribution,
  every benefit allocation and the current balance. Printed at the till, emailed, or on the storefront.
- **Governance controls** — the treasurer can see the group; a member sees only their own line unless the
  group's constitution says otherwise; every payout requires the configured approvals; every adjustment
  is reason-coded and audited. This is other people's savings.

## Business rules
- **Money held for a customer is a liability, not revenue.** Lay-by deposits, stokvel contributions and
  account credits post to liability accounts through Stage 07's posting rules, and release to revenue
  only on delivery of goods. Reconcile each to its control account continuously, like every other
  sub-ledger.
- Lay-by and stokvel stock reservations reduce available stock and are visible as such — a stokvel with
  200 members reserving December hampers must not read as sellable stock in November.
- No balance is ever edited. Corrections are reason-coded, audited transactions.
- Every contribution produces an individual receipt with a running balance. A stokvel member who cannot
  see their own number does not trust the scheme, and the treasurer becomes your support queue.
- Offline: contributions and instalments can be taken offline against the last-known balance, queued and
  reconciled on sync. A payout that would exceed a stale balance requires connectivity — the amounts are
  too large to guess at.

## Tests / acceptance
- Account: invoice → part payment → credit note → statement, with ageing correct at every step
- Credit limit enforced at tender including a simulated unsynced offline account sale
- Lay-by full lifecycle: deposit → three instalments → completion produces exactly **one** stock issue
  and **one** revenue recognition, dated at completion, with correct cost of sale
- Lay-by cancellation: reservation released, forfeiture posted per policy, stock sellable again
- Lay-by price protection across a shelf-price change mid-term
- Stokvel time-weighted benefit allocation matches a hand-computed fixture across members who joined at
  different points in the cycle — the single most dispute-prone calculation in the module
- Stokvel payout into a hamper: stock issued, group and member balances reduced, tax and margin correct
- Member leaving mid-cycle: pro-rata calculation matches the fixture
- Liability reconciliation: lay-by deposits, stokvel balances and account credits each equal their GL
  control account after 5,000 seeded transactions
- Permission test: a stokvel member cannot see another member's balance; a treasurer can see the group

## Exit checklist
- [ ] All three schemes demonstrable end to end on seed data, with correct postings at every step
- [ ] Liability accounts reconcile to the cent
- [ ] Member and group statements print, email and display correctly
- [ ] Reserved lay-by and stokvel stock excluded from available stock
- [ ] Offline contribution capture and reconciliation proven
- [ ] Replication registry updated, `docs/PROGRESS.md` + ADRs updated, committed
