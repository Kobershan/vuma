# STAGE 10 — Sales Management & Promotions: Pricing, Specials, Returns

**Status:** DONE (merged to `main` 2026-08-16) · **Depends on:** 09 · **Reference reading:**
`docs/DATA_MODEL.md` §1–§3 (mandatory columns, types, schema conventions), `docs/CONVENTIONS.md`
§1–§5, `docs/TESTING.md` §3 (the money failure modes this stage is made of), `docs/DECISIONS.md`
ADR-016 and `CLAUDE.md` §7 rule 12 (no module names a GL account), ADR-065 (document numbering),
ADR-067 (optional `Money` is two columns), ADR-070 (a missing posting rule does not refuse a
movement), **ADR-072** (the field this stage fills in), **ADR-073** (a refused stock issue does not
fail the document).

## Objective

Stage 09 built a till that sells at whatever price the caller hands it. This stage decides **what that
price should be** — and gives the shop the two things it cannot trade without: a promotions engine
that makes a special a piece of configuration rather than a code change, and a return path that takes
goods and money back without editing a receipt that a customer is holding.

ADR-072 is the hinge. It says `SaleLine.UnitPrice` is supplied and stored as sold, and that Stage 10
"changes what fills the field in, not the field". This stage honours that literally: `AddSaleLineCommand`
keeps its shape, POS keeps its "price arrives with the line" path — which a weighed, open-price or
manually reduced line needs forever — and a new resolver becomes what the screen calls to get the
number it puts there.

### Scope split — this stage is 10 of 10 + 10c

`ROADMAP.md` lists five features against Stage 10: quotes, invoices, returns, specials engine and
sales analytics. That is not one focused session, and the roadmap's own rules of engagement say to
split rather than silently under-deliver. Split as **ADR-074**:

- **Stage 10 (this document)** — price lists and price resolution, the promotions/specials engine,
  and returns/credit notes. Everything Stage 09's till and Stage 10b's customer money are blocked on.
- **Stage 10c** — quotes, invoices and sales analytics. Sales *documents* and their reporting. None of
  it is a dependency of 10b, 11 or 12; Stage 14 (order management) is the natural neighbour and Stage
  29 owns read models and KPIs, so analytics built here would be built twice.

### What this stage does not own

- **No customer credit, lay-by or stokvel.** ADR-055 gave money held for a customer its own module,
  10b. This stage prices and returns; it settles nothing against an account. POS's declared
  `TenderType.CustomerAccount` stays unhandled.
- **No GL account, anywhere.** A return raises a financial event with one amount; Stage 07's posting
  rules engine decides the accounts (rule 12).
- **No loyalty earn/burn.** Stage 20. A promotion here is priced off the basket, not off a member's
  points balance.
- **No order, allocation or backorder.** Stage 14.
- **No margin or markdown planning.** Stage 15 plans them; this stage only executes a price.
- **No new till screen.** Every deliverable is an endpoint, per R3, for the same Windows/WPF reason
  Stage 09 stopped where it did (§4.3, ADR-031).

## Deliverables

**`sales` module** (schema `sales`, seven tables)

- `PriceList` — a named set of prices. Unique `Code` per tenant (upper-cased, like `Store.Code`),
  `Name`, `Currency`, `PriceListKind` (`Retail`, `Wholesale`, `Staff`), a **`PricesIncludeTax`** flag,
  `Priority`, `EffectiveFrom`/`EffectiveTo`, `IsActive`, optional `StoreId` scope. Deactivate, never
  delete — a completed sale must still be explicable next year.
- `PriceListLine` — one price. Item **or** variant (exactly one, the `SaleLine` rule), `UnitPrice`,
  and a `MinimumQuantity` that makes quantity breaks a row rather than a feature. Unique on
  (price list, item/variant, minimum quantity).
- `Promotion` — a special. `Code`, `Name`, `PromotionKind`, the reward parameters, `Priority`,
  `IsExclusive`, `EffectiveFrom`/`EffectiveTo`, an optional day-of-week + time-of-day window for a
  happy hour, optional `StoreId`, `IsActive`. Promotions are **configuration**: adding one is a row,
  never a deployment.
- `PromotionLine` — what a promotion applies to: item, variant, or category, with the quantity the
  reward needs.
- `SalesReturn` — the document that takes goods back. References the original `Sale` by id,
  `ReturnNumber` from ADR-065's sequence (series `RTN`), `SalesReturnStatus`, `Reason`,
  `RefundTenderType`, the money, and who authorised it. A return is **a new document, never an edit**
  (§7 rule 7) — Stage 09 left `ck_sale_lines_quantity_positive` in place precisely so nobody could
  slide a negative line into a completed sale.
- `SalesReturnLine` — one line coming back. References the original `SaleLine`, carries a positive
  `Quantity`, the original line's `UnitPrice`, and its **own** net/tax/gross computed for the returned
  quantity at the original line's effective tax.
- `PriceOverrideLog` — every time an operator sold at something other than the resolved price, with
  who, why and the gap. The oldest shrinkage pattern there is, and unauditable if it is not a row.

**Price resolution** (`Application/Abstractions/Sales/SalesPorts.cs`)

- `IPriceResolver.ResolveAsync(...)` → the price a given item/variant should sell at, for a quantity,
  at a store, on a date, in a currency: the winning price list line, then every promotion that applies,
  then the resulting unit price, discount and the explanation of how it got there.
- `PriceResolution` carries **why**, not just what — the price list and the promotions that fired, in
  order. A cashier who cannot tell a customer why the till says R18.99 will override it.
- The port lives in `Application.Abstractions.Sales` and is implemented in `Application/Sales`,
  following exactly the `ITaxCalculator` pattern Stage 09 established for a cross-module call.

**Promotion engine** (`Application/Sales/Pricing/`)

- `PromotionEngine` — pure, synchronous, no I/O, given a basket and the candidate promotions. Pure so
  that the table-driven tests `docs/TESTING.md` §3 demands are possible at all.
- Five kinds, which cover what a South African shop actually runs:
  `PercentageOff`, `AmountOff`, `FixedPrice`, `MultibuyForAmount` (3 for R50), `BuyXGetYFree`.
- Deterministic resolution: filter to applicable → order by `Priority` then `Code` → apply; an
  exclusive promotion that fires stops the rest. **The same basket always yields the same price**, and
  there is a test that runs the candidates in shuffled order and asserts it.

**Commands** (all through the dispatcher, §7 rule 2): create/update/activate/deactivate a price list
and its lines, create/update/activate/deactivate a promotion, `ResolvePriceCommand`'s query
equivalent, `RecordPriceOverrideCommand`, and `CreateSalesReturnCommand` +
`CompleteSalesReturnCommand`.

**Queries:** resolve a price (with explanation), list effective promotions for a store today, list a
sale's returns, list returns for a period, list price overrides for a period.

**Integration with what already exists**

- **Inventory (08).** A completed return puts stock back through `IStockLedgerPoster` as a receipt at
  the original cost. It follows ADR-073's shape: if the ledger refuses, the return still completes and
  the line records the refusal — the customer is standing there and the goods are already back over
  the counter.
- **Finance (07).** A completed return raises `sales.return.completed` through
  `IFinancialEventPoster`, naming an event type and amounts, never an account. Follows ADR-070: no
  posting rule means the return still completes.
- **POS (09).** `SellableItemResolver` gains nothing; the till calls `IPriceResolver` and passes the
  answer into the unchanged `AddSaleLineCommand`. **No Stage 09 file changes shape.**

## Business rules

1. **Tax is computed and stored per line, and no consumer may recompute it from a document total.**
   This is the rule the Stage 09 handoff asked for as its own ADR (item 7) — **ADR-075**. A return's
   tax is derived from the original line's stored tax, pro-rata to the returned quantity, *not* by
   putting the refund through today's tax rate. Yesterday's receipt is not restated by tomorrow's
   budget speech.
2. **Round once, on the extended amount.** Resolve the unit price at full precision, multiply by
   quantity, and round the result — never round the unit price and multiply. This is §4.14's defect,
   found in Stage 09, and it is the single most likely bug in this stage. The 0.005-boundary test is
   mandatory.
3. **A price list line beats nothing; a promotion beats a price list.** Resolution order is: the
   highest-priority active list scoped to the store → its quantity break for the quantity being sold →
   then promotions. A promotion never *raises* a price.
4. **No promotion can produce a negative line.** A stacked set that would take a line below zero
   clamps at zero and records that it clamped. A till that pays a customer to leave is a defect.
5. **A return can never exceed what was sold.** The cumulative returned quantity per original sale
   line, across every return against it, is `≤` the quantity sold. Enforced in the aggregate *and* by
   a database constraint, because this one is money.
6. **A return refunds what was actually charged.** The unit price comes from the original line, not
   from today's price list and not from the current promotion. A customer who bought on special gets
   the special back.
7. **Only a completed sale can be returned**, and a voided sale can never be.
8. **Every price a resolver returns is explicable.** `PriceResolution.Explanation` is not optional and
   is not a log line.
9. **An override is a row, not a warning.** Selling off-price writes `PriceOverrideLog` with the
   operator, the resolved price, the actual price and a reason.
10. **Currency is the store's, bound at the document.** §4.13's defect in Stage 09 was a sale that
    accepted a foreign currency and then bricked the terminal. A price list in the wrong currency for
    the store is refused at resolution, with a `ProblemDetails` the caller can act on — never a 500.

## Tests / acceptance

- **Unit, table-driven, on the engine** — the five promotion kinds, priority ordering, exclusivity,
  the shuffle-invariance test, the clamp-at-zero rule, and the quantity-break boundaries (exactly at
  the break, one below, one above).
- **Unit, on the money path** — the 0.005 rounding boundary in both directions, a weighed quantity
  (`decimal(18,6)`) through the extended-price path, and a tax-inclusive price list against a
  tax-exclusive one for the same item resolving to the same gross.
- **Unit, on returns** — over-return refused at the aggregate, partial returns accumulating correctly,
  a return of a discounted line refunding the discounted price, tax pro-rata.
- **Integration** — resolution against a real database with overlapping lists and promotions; a
  return posting stock and raising its financial event; a return whose stock posting is refused still
  completing (ADR-073); the database constraint refusing an over-return that bypassed the aggregate.
- **Architecture** — `sales` names no GL account; the new commands are classified for the read-only
  guard; the new entities are in the replication registry.
- Coverage ≥ 80% line on the stage's Domain + Application code (§8).

## Exit checklist

- [x] `dotnet build -c Release` — 0 warnings, 0 errors across 15 projects
- [x] Full suite green, with the new tests, 0 skipped — **930 passed** (558 unit, 31 architecture,
      341 integration), up from 835
- [x] EF migration `20260816064906_Sales` generated and applied; `Down` tested reversible by
      `MigrationTests`, which migrates the whole chain to `0` and back
- [x] All new endpoints in `/openapi/v1.json`, verified against a booted host — **24 operations across
      19 paths** under `/api/v1/sales`, every one carrying a summary and the full
      `400/401/403/422/500` set
- [x] RBAC permissions registered (7, in `SalesPermissions`); `SalesModuleManifest` registered
- [x] `sales.return.completed` raised through `IFinancialEventPoster`, no account named — the
      architecture test that proves it is `No_application_code_outside_finances_own_ports_references_a_GL_account`,
      which scans the whole Application assembly and therefore covered this module the moment it existed
- [x] Stock returned through `IStockLedgerPoster`, refusal path recorded on the line (ADR-073)
- [x] Entitlement flag declared (`sales`, non-core); metering counters follow from declaring the
      schema — `UsageCounterSource` groups on the schema half of `schema.table` from the audit trail,
      so the module gets its counter by existing (§7 rule 16 holds: the count is a count)
- [x] Replicated entities registered (7) and `docs/SYNC_AND_BACKUP.md` §3 updated **in the same commit
      as the entities**, which is the practice the Stage 09 review asked for after finding that table
      25 entities short across two stages
- [x] `docs/DATA_MODEL.md` §4h written; ADR-074 and ADR-075 appended
- [x] Seed data — a retail price list with a quantity break, two live promotions of different kinds,
      and one completed partial return. All five demo journals balance, including the return's two
- [ ] **All six agents run — NOT DONE.** See `docs/PROGRESS.md`; this is the one item this stage
      cannot tick, and it is the same gap that reopened Stage 09. Recorded rather than glossed
- [x] `docs/PROGRESS.md` updated, committed as `feat(stage-10): sales management & promotions`

### Coverage

**83.14% line on Domain/Sales + Application/Sales** (1016/1222), over the §8 bar of 80%. The engine and
the resolver — where the money is decided — are at 98.53% and 97.56%. What is uncovered is mostly the
activate/deactivate command handlers and the query handlers, which are three-line delegations.

## Notes for later stages

- **Stage 10b (accounts, lay-by, stokvels)** settles the `CustomerAccount` tender POS declares, and a
  lay-by is priced through this stage's resolver at the moment it is laid by, not when it is collected.
- **Stage 10c** picks up quotes, invoices and sales analytics. A quote is a priced basket with an
  expiry — this stage's resolver is what prices it, and a quote must snapshot the resolution so an
  expired special does not silently reprice it.
- **Stage 11 (import)** ingests price lists and specials in bulk. `PriceListLine`'s uniqueness rule is
  what makes an idempotent re-import possible.
- **Stage 15 (planning)** owns markdown planning; a planned markdown becomes a `Promotion` row here.
- **Stage 20 (loyalty)** prices member rewards. The engine's `Priority`/`IsExclusive` pair is the
  extension point — a loyalty reward is a promotion whose applicability test asks about a member.
- **Stage 29 (reporting)** owns the sales KPI cube. This stage deliberately ships no analytics read
  model for it to conflict with.
