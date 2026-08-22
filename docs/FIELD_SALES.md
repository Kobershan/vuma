# FIELD SALES — the rep module

> **The requirement, in the operator's words.** "We need a rep module for reps on the road so they can
> create pro forma orders and pro forma credit notes. All orders by reps, they should be able to see
> how much stock is available. Invoicing by reps must be approved by management before being turned
> into an invoice. The rep module should have the ability to check how much reps do per month and
> compare that over a time period."

This document is the contract for Stage **14b**, and is referenced by Stages 10c, 14 and 08c.

---

## 1. What a rep is

A rep is a user with a sales territory, a device that is offline more often than online, and no
authority to commit the business to a price or to release stock. Everything a rep produces is a
**proposal**. Management turns proposals into documents.

```
Rep (on the road, phone/tablet)          Management (back office)
────────────────────────────────         ─────────────────────────
 sees group-wide available stock
 captures a PRO FORMA ORDER      ──────▶  reviews, prices, approves
                                          ├─ approve → SALES ORDER (stock reserved)
                                          ├─ amend   → back to rep with a reason
                                          └─ reject  → closed, nothing reserved
 captures a PRO FORMA CREDIT NOTE ─────▶  approve → CREDIT NOTE (Stage 10's returns path)
```

---

## 2. Pro forma documents

A pro forma is a **quotation-shaped proposal**. It is not a financial document.

- It **posts nothing** — no GL, no AR, no stock ledger, no VAT return (ADR-107).
- It **reserves nothing.** The quantities on it are not held for the customer. A rep is told this
  plainly on the document: the stock figures are indicative at the time of capture.
- It **has its own number sequence per company** (`PF-` / `PFC-`), never an invoice number.
- It **expires.** Default 7 days, configurable per tenant. An expired pro forma must be re-priced
  before it can be approved, because prices and promotions move.
- It **carries a price snapshot**: the price list, the promotion set and the pack size resolved at
  capture, so the approver can see what the rep quoted and what it costs today, side by side.
- A **pro forma credit note** is the same shape against a supplied document: it names the original
  invoice and lines, and a reason code. It posts nothing until approved, at which point it becomes a
  Stage 10 credit note and reverses inside the invoice's own company.

## 3. Approval

Approval goes through Stage 05's `IApprovalService` — the rep module implements no approval logic of
its own (`CLAUDE.md` §7 rule 13).

- The policy is configurable per company: by value, by margin floor, by customer credit state, by
  discount depth. A default policy ships: **every rep pro forma requires approval**, regardless of
  value.
- The approver sees, on one screen: the quoted price vs the current price, the margin, the customer's
  **group** credit position (`MULTI_COMPANY.md` §6), current availability per company, and what the
  approval will reserve.
- **Approval is the moment stock is committed.** On approval the resulting sales order takes a
  reservation against the sourcing companies (§4), and a credit-group consumption if the order is on
  account.
- Approval is per document, not per rep, and it is audited: who, when, from where, and what changed
  between what the rep quoted and what was approved.
- Rejection and amendment both return to the rep with a reason. A rep is never left guessing why.

## 4. Stock the rep can see

- A rep sees **available** quantity, group-wide, per company — never on-hand, never cost, never
  margin unless their role grants it (ADR-109).
- Availability is a cached read on the device with the timestamp shown. A rep who has been offline for
  four hours sees "as at 06:12" rather than a confident wrong number.
- Availability is not a promise, and the UI says so, because nothing is reserved until approval.
- The rep's device is an offline-first client: pro formas are captured offline, queued and replayed
  through the same idempotent sync batch path as the till (`PROGRESS.md` §4.11's fix), never a
  bespoke endpoint.

## 5. Rep performance

> "Check how much reps do per month and compare that over a time period."

- **A rep performance snapshot is a closed-period read model**, the same shape as the supplier
  scorecard (ADR-084): computed for a closed period, stored, never recomputed silently behind a report
  that was already printed (ADR-110).
- Per rep, per period, per company **and** rolled up to the group:
  - pro formas captured — count and value
  - conversion: approved / rejected / expired, and the value that converted
  - invoiced value, credited value, **net** value
  - margin where the rep's role may see it
  - collections against their book, if the tenant tracks that
  - active customers, new customers, dormant customers, call coverage
- **Comparison is first-class, not a spreadsheet job**: this month vs last, vs the same month last
  year, vs the rep's target, vs the team. The API returns the comparison figures and the variance, so
  the Android app and the desktop show the same numbers.
- Targets are set per rep, per company, per period, and are versioned — changing a target does not
  rewrite what last month was measured against.
- **Commission is deliberately out of scope here.** It is payroll's, and it belongs with Stage 25/26.
  What this stage owes them is a clean, auditable net-sales figure per rep per period.

## 6. Territory

- A rep is assigned customers, and optionally a geography (province / city / suburb — the same
  hierarchy the consolidated pick waves use, `STAGE-13b`).
- Territory decides what a rep may see and quote, and it is enforced server-side. A rep who guesses a
  customer id gets a 403, not a document.
- A customer may be covered by more than one rep across companies — the credit group is still one.

## 7. The rules a reviewer checks

`field-sales-guard` (`.claude/agents/field-sales-guard.md`) reviews against exactly these:

1. No pro forma path writes to the GL, AR, the stock ledger or a reservation. Prove it by absence of
   the poster/reserver dependency, not by reading the happy path.
2. No pro forma can become an invoice without an approval record, including through the sync replay
   path, the import pipeline and any admin endpoint.
3. Approval reserves before it commits credit, and both are inside one transaction.
4. A rep cannot read cost, margin or another territory's customers without the permission that grants
   it — structurally, not by a filter a caller may forget.
5. Availability shown to a rep is *available*, not on-hand, and carries its as-at timestamp.
6. Performance snapshots are closed-period and immutable; a re-run for a closed period produces the
   same numbers or a new version, never a silent overwrite.
7. Offline replay of a pro forma is idempotent — the same operation id twice produces one document.
