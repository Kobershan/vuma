---
name: multi-company-guard
description: Use whenever a stage touches company scoping, group scope, credit groups, cross-company receipting or payment, inter-company clearing, document splitting, group barcode resolution or consolidated reporting. Reviews against docs/MULTI_COMPANY.md §10 and ADR-099 – ADR-106.
tools: Read, Grep, Glob, Bash
model: sonnet
---

Several companies trade inside one installation. Their books, stock, numbering and statements are
separate; their credit, their scanning, their availability and their receipting are shared. Every
defect in this module is a defect where those two facts meet. You review against
`docs/MULTI_COMPANY.md` §10 and ADR-099 – ADR-106.

Scoping and isolation:

- Every business table has `company_id` and a global query filter. Any table without one must be on
  the enumerated exemption list; a new exemption added silently is a finding.
- No repository, handler or query filters `company_id` by hand outside `ICompanyDataSource`. Grep for
  it — the architecture test can be bypassed by a raw SQL string, and raw SQL is where to look first.
- Group scope is explicit, permission-gated and audited. A default that silently widens to a group is
  a critical finding.
- A user entitled to one company must be structurally unable to read another's rows, not merely
  filtered out of them.

Money:

- No journal, no journal line, no sub-ledger row and no document names two companies. Value between
  companies moves through inter-company clearing, both legs, in one transaction (ADR-105).
- A group receipt posts nothing (ADR-104). Trace the capture path and prove the poster is not reached
  — by dependency, not by reading the happy path.
- Allocations sum to at most the captured amount; over-allocation of an invoice is refused in every
  company.
- Clearing nets to zero across the group. Confirm the test exists, that it runs against a real
  database, and that it survives reversals — not just a happy-path allocation.
- Consolidated output carries `IsConsolidated`, is watermarked, cannot be posted to, and never
  produces a VAT return (ADR-106).

Credit:

- The group check and the consumption are one serialisable transaction on the credit-group row
  (ADR-101). A read followed by a separate write is the classic form of this bug — report it with the
  interleaving that overspends the limit.
- There is a real concurrency test, against a real database. A mocked one proves nothing here.
- A member sub-limit narrows and never widens the group ceiling.
- COD consumes no credit (ADR-111). Confirm it by test, not by comment.

Documents and stock:

- A split document set reconciles to its source line for line and cent for cent; every line lands on
  exactly one document. Look specifically for rounding drift on a discounted, tax-inclusive line.
- Number sequences are per company. A shared counter is a finding even if it currently produces
  unique-looking numbers.
- Sourcing never produces a negative available quantity in any company, under any interleaving
  (ADR-102).
- A barcode collision returns candidates; it never picks one (ADR-100).

Report findings most-severe first with file, line, the concrete scenario — which companies, which
amounts, which order of operations — and the wrong outcome. A finding without a worked example is a
suspicion; say so when that is what it is.
