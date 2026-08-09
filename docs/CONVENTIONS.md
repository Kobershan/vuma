# CONVENTIONS — Zenith Retail

Formatting is settled by `.editorconfig` and is not worth discussing. This file covers the decisions
an analyzer cannot make for you.

## 1. Projects and namespaces

Namespace matches folder path from the project root: `src/ZenithRetail.Domain/Sales/Pricing/` →
`ZenithRetail.Domain.Sales.Pricing`. File-scoped namespaces. One public type per file, named after
the file.

Module code is grouped by **module first, then layer within it** — `Domain/Inventory/StockLedger.cs`,
not `Domain/Entities/Inventory/StockLedger.cs`. A module should be readable, and later extractable,
as one unit (ADR-010).

## 2. Database naming

`snake_case` throughout, because PostgreSQL folds unquoted identifiers to lower case and fighting
that produces quoted identifiers everywhere.

- **Schema name = module name**, lower case: `sales`, `inventory`, `finance`, `hr`, `loyalty`.
- Tables are plural: `sales.transactions`, `inventory.stock_ledger`.
- Columns `snake_case`; foreign keys are `<entity>_id`.
- Indexes `ix_<table>_<columns>`, unique `ux_<table>_<columns>`, constraints `ck_<table>_<rule>`.
- Every table carries the `Entity` base columns (`CLAUDE.md` §7 rule 3). Never re-declare them.
- **No cross-schema foreign keys.** Modules reference each other by ID and resolve through published
  contracts or domain events. A foreign key across a module boundary is the thing that makes the
  monolith unextractable, and there is an architecture test for it.

## 3. Naming in code

| Thing | Shape | Example |
|---|---|---|
| Command | `<Verb><Noun>Command` | `VoidSaleCommand` |
| Query | `Get<Noun>Query`, `List<Noun>Query` | `GetStockBalanceQuery` |
| Handler | `<Message>Handler` | `VoidSaleCommandHandler` |
| Domain event | past tense, `<Noun><VerbEd>` | `SalePosted`, `StockAdjusted` |
| Validator | `<Message>Validator` | `VoidSaleCommandValidator` |
| Port (interface) | `I<Capability>` in Application | `IReceiptPrinter` |
| Adapter | `<Technology><Capability>` in Infrastructure | `EscPosReceiptPrinter` |
| DTO | `<Noun>Dto`, or `<Noun>Response` / `<Noun>Request` at the edge | `StockBalanceDto` |
| Permission | `module.entity.action` (ADR-013) | `inventory.stocktake.approve` |
| Test | `Method_or_rule_stated_as_a_sentence` | `Refuses_to_combine_two_currencies` |

Async methods end in `Async` and take a `CancellationToken` as the last parameter, defaulted at the
public edge and required internally.

## 4. Commands and queries

- Every command declares `[CommandSideEffect(SideEffect.Write)]` or `SideEffect.ReadOnly`. This is
  not optional and not a style preference — read-only enforcement (ADR-028) is a single pipeline
  interceptor that reads it, and an unclassified command **fails the build**.
- Commands and queries are records. They are messages, not objects with behaviour.
- A handler does one thing. If it needs a second handler's logic, extract a domain service; do not
  call one handler from another.
- Validation lives in a FluentValidation validator, not in the handler.
- Handlers return data, never HTTP concepts. Endpoints map results to status codes.

## 5. Errors

- Domain rule violations throw a domain exception derived from `DomainException`. They are expected
  and are mapped to `422`.
- Infrastructure failures propagate. Do not catch what you cannot handle to log it and rethrow — the
  pipeline logs once, centrally, with correlation.
- Every API error is a `ProblemDetails` with a **stable machine-readable code** (`LICENCE_READ_ONLY`,
  `LICENCE_MODULE_NOT_ENABLED`, …). The code is the contract; the message is for humans and may be
  reworded freely.
- Never leak a tenant's billing status to that tenant's own customers through the public API — a
  neutral `503` is deliberate (ADR-028).

## 6. Money, quantities and time

- Money is `Money`, never a bare `decimal`. Quantities are `Quantity`. There is no exception for
  "just this one internal calculation".
- Round once, at the point a document line is finalised — never in the middle of a calculation.
- All timestamps are `DateTimeOffset` in UTC, `timestamptz` in the database, converted at the edge
  only (`CLAUDE.md` §7 rule 9). No `DateTime.Now` anywhere; take an `IClock` so tests can control it.
- IDs are `UuidV7.NewGuid()`, never `Guid.NewGuid()`.

## 7. Tests

- Name the rule, not the method: `A_posted_invoice_cannot_be_edited`.
- Arrange/act/assert, no comment headers for them.
- One behaviour per test; `[Theory]` for the same behaviour across inputs.
- Integration tests use real PostgreSQL through Testcontainers and real migrations. A mocked
  `DbContext` proves nothing about a query that has to run against Postgres.
- Fixed seeds for Bogus, so a failure reproduces.
- A test asserting an error message should assert the *substance* — the codes or values that carry
  the meaning — not the full sentence, which will be reworded.

## 8. Comments

Comment the **why**, never the what. `// increment the counter` above `counter++` is noise; a note
explaining that a counter rolls into the next millisecond rather than wrapping, and what would break
otherwise, is worth its line.

Every public type and member on an API surface carries an XML doc comment (`CLAUDE.md` §7 rule 11) —
enforced as an error in Domain and Application.

Where code exists because of a decision, cite it: `// ADR-005: the ledger is append-only`. The next
session has no memory of this one and will otherwise "fix" it.

## 9. Commits

```
<type>(stage-NN): <summary in the imperative>
```

`feat`, `fix`, `docs`, `test`, `refactor`, `chore`, `ci`. The stage scope is what lets
`PROGRESS.md` be reconciled against history. Stage completion commits are exactly
`feat(stage-NN): <stage title>` (`CLAUDE.md` §0 step 10).

Never commit real credentials. `appsettings.Development.json` is git-ignored; `.env.example` carries
placeholders only.
