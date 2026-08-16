# STAGE 09 — POS Terminal & Hardware

**Status:** DONE (2026-08-15), *WPF till screen deferred* · **Depends on:** 08 (stock ledger), 07 (posting rules), 06 (catalogue),
02 (terminals, operators) · **Reference reading:** `docs/HARDWARE.md` (written by this stage),
`docs/DATA_MODEL.md` §1–§3, `docs/CONVENTIONS.md` §1–§5, `docs/API_STANDARDS.md` §8,
`docs/DECISIONS.md` ADR-005 and ADR-068 to ADR-071 (the ledger this stage moves), ADR-016 and
`CLAUDE.md` §7 rule 12 (no module names a GL account), ADR-028 (read-only never happens by accident),
ADR-065 (document numbering).

## Objective
The till. A sale is opened at a terminal, lines are rung up, tenders are taken, a receipt is printed,
the drawer opens, and at the end of the shift the cash is counted against what the system says should
be there. Stage 08 said what is on the shelf and what it is worth; this stage is the thing that takes
it off the shelf and takes the money for it.

R1 governs every decision in this stage: **POS never stops.** A till that refuses to sell because
finance is misconfigured, because the stock projection disagrees with the shelf, or because the
network is down, is a broken till. Everything here is designed so that the sale completes and the
discrepancy is recorded for somebody to reconcile, rather than the customer being turned away.

This stage owns **the sale, the tender and the shift**, not the things around them:
- **No pricing.** Stage 06 deliberately shipped no price (`STAGE-06-master-data.md` scope note), and
  the price list, promotions and specials engine are Stage 10. A sale line here carries the price it
  was *sold at*, supplied by the caller. See ADR-072.
- **No returns, quotes or invoices** — Stage 10. A line's quantity is positive; a mistake at the till
  is a void, which is a different thing from a return the customer walks back in with.
- **No lay-by, no customer account tender, no stokvel.** ADR-055 gave money held on behalf of a
  customer its own module at Stage 10b, and `ROADMAP.md`'s one-line summary for this stage
  ("…lay-by, cash-up") predates it. This stage ships the `TenderType` members those modules settle
  against and nothing behind them. See the roadmap correction in `PROGRESS.md`.
- **No WPF shell.** `VumaRetail.Desktop` cannot be built or run on this machine (ADR-031,
  `PROGRESS.md` §4.3), and the design system it must consume is Stage 08b, still NOT_STARTED. R3
  says nothing exists in a UI that is not reachable over the API first; this stage builds that API,
  in full, and the screen that renders it is deferred with the rest of the Windows surface.
- **No GL account.** A tendered sale raises `pos.sale.tendered`; Stage 07's posting rules engine
  decides the accounts. The rule is already seeded and has been waiting since Stage 07.

## Deliverables

**`pos` module** (schema `pos`, five tables)

- `TillSession` — a shift at one terminal. `TerminalId`, `OperatorUserId`, `OpeningFloat`,
  `OpenedAt`, and on close `CountedCash`, `ExpectedCash`, `Variance`, `ClosedAt`, `ClosedBy`.
  `TillSessionStatus` is `Open` or `Closed`; a closed session is never reopened, and a second open
  session for the same terminal is refused. This is the cash-up: expected cash is computed from the
  session's own cash tenders plus the float, never from a counter somebody can edit.
- `Sale` — the aggregate root. `SaleNumber` (ADR-065's sequence, series `SALE`), `TillSessionId`,
  `TerminalId`, `OperatorUserId`, `LocationId` (the stock location the goods leave), optional
  `CustomerId` (bare `uuid` into `partners`, never a foreign key), `Currency`, `SaleStatus`
  (`Open`, `Parked`, `Completed`, `Voided`), the `Net`/`Tax`/`Gross` totals, `AmountTendered`,
  `ChangeGiven`, `OpenedAt`, `CompletedAt`, `VoidedAt`, `VoidReason`.
- `SaleLine` — `ItemId`/`ItemVariantId` (exactly one, `StockItemReference`), a `Description` snapshot
  taken at ring-up, `Quantity`, `UnitPrice`, `DiscountAmount`, `TaxCode`, and the computed
  `LineNet`/`LineTax`/`LineGross`. Also carries the outcome of its stock issue:
  `StockIssueStatus` (`Pending`, `Posted`, `Refused`), `StockLedgerEntryId` and `StockIssueNote`.
- `SaleTender` — `TenderType` (`Cash`, `Card`, `Voucher`, `MobileMoney`, `CustomerAccount`),
  `Amount`, optional `Reference` (a card authorisation code, a voucher serial), `CapturedAt`.
  `IImmutableRecord`: a tender is money that changed hands, and it is reversed by another tender,
  never edited.
- `ReceiptPrint` — append-only, one row per print. `SaleId`, `PrintedAt`, `PrintedBy`,
  `IsReprint`, `Reason`. `IImmutableRecord`. Receipt reprints are the oldest till fraud there is;
  R6 says who did what, and a print is a thing somebody did.

Every item, variant, customer, terminal and user reference is a plain `Guid` with an index and never
a foreign key into another module's schema (`CONVENTIONS.md` §2). The catalogue is reached through
Stage 06's published repository ports, the same way `StockKeepingUnitResolver` does it.

**Application** — `SaleTotals` is the single place line and sale arithmetic lives, so the
net/tax/gross relationship exists once. Commands: open/park/resume/void a sale, add and void a line,
tender, complete, open and close a till session, record a receipt print. Queries: get a sale, list
parked sales at a terminal, build a receipt document, get a till session and its cash-up, list
unreconciled stock-issue failures, and resolve a scanned barcode to a sellable line.

**`VumaRetail.Hardware`** — the project `CLAUDE.md` §5 has always listed and no stage had yet built.
Cross-platform (`net9.0`) because the abstraction and the wire formats are, with the Windows-only
device transports deferred (below):
- `EscPosRenderer` — a receipt document to ESC/POS bytes, and to plain text for a dev printer. Pure,
  deterministic, fully tested; this is the part that is genuinely hard to get right and has nothing
  to do with Windows.
- `IReceiptPrinter` with `NetworkReceiptPrinter` (raw TCP 9100, which is how most till printers are
  actually attached in a store) and `TextFileReceiptPrinter` for development.
- `ICashDrawer` with `PrinterKickCashDrawer` (the ESC/POS pulse, because the drawer is wired to the
  printer, not the PC) and `NullCashDrawer`.
- `IPaymentTerminal` with `SimulatedPaymentTerminal`, and `IWeighingScale` with
  `SimulatedWeighingScale` — both deferred integrations in the sense `PROGRESS.md` §3 means, since
  there is no acquirer contract and no scale on this machine.
- `ScannedBarcode` — parses a scan, including **price- and weight-embedded EAN-13** barcodes, which
  is how every deli, butchery and produce scale in a South African supermarket labels its output.
  Pure and fully tested.

**Infrastructure** — EF configurations for all five tables, the `Pos` migration, four repositories,
`AddVumaPos`.

**Web** — endpoints under `/pos`, covering every command and query above (R3).

**Docs** — `docs/HARDWARE.md`, which §4.1 of `PROGRESS.md` has had listed as owed by this stage since
Stage 00.

## Business rules

1. **A sale never fails because something downstream is misconfigured.** A missing posting rule is
   logged and the sale stands (the same call ADR-070 made for a stock movement). A stock issue the
   ledger refuses is recorded on the line and the sale stands. The customer has already walked out
   with the goods; refusing to record that fact does not undo it.
2. **Stock still cannot go negative** (Stage 08 rule 4, ADR-005). Rule 1 does not become a licence to
   punch through it. The sale is recorded, the issue is *not* posted, and the line carries
   `StockIssueStatus.Refused` with the reason — a real, queryable list of "the shelf and the system
   disagree" that a manager reconciles with a stocktake. See ADR-073.
3. **Money is arithmetic, not a stored opinion.** A line's net, tax and gross are computed from
   quantity, unit price, discount and the tax rule effective on the sale's date; the sale's totals
   are the sum of its live lines. A voided line contributes nothing.
4. **A sale must balance before it completes.** Tenders must sum to at least the gross; change is
   the difference and is only ever given in cash. A sale with no lines does not complete.
5. **Tax comes from the tax rules engine, never a constant** (`CLAUDE.md` §9). The line stores the
   tax code and the computed amounts, so a rate change tomorrow does not restate yesterday's sale.
6. **A completed or voided sale is immutable.** No line added, no line voided, no tender captured.
   It is corrected by a Stage 10 return, not an edit.
7. **A tender is immutable once captured**, and a sale is voided rather than deleted.
8. **One open till session per terminal.** A sale must belong to an open session, and closing a
   session with sales still open is refused — otherwise the cash-up is a count against a moving
   target.
9. **The expected cash is derived, never entered.** Opening float plus the session's own cash
   tenders minus change given. The variance is the counted amount less that, recorded and signed;
   nothing is "corrected" to make it zero.
10. **Every receipt print is recorded, and every print after the first is a reprint** carrying a
    reason.
11. **No module names a GL account** (rule 12). A completed sale raises `pos.sale.tendered` with
    `Net`, `Tax` and `Gross`; the posting rule decides where they land.
12. **A sale's id may be minted by the caller.** UUID v7 is offline-safe by design (§4), so a
    terminal that opened a sale while the network was down replays it under the id it already
    printed on the customer's slip, and a replay is idempotent rather than a second sale.

## Tests / acceptance

- **Unit** — line arithmetic across quantity, discount and both tax treatments, including a discount
  that exceeds the line and a tax that does not divide evenly; the sale status machine refusing every
  illegal transition; tender balancing and change; cash-up variance in both directions; ESC/POS byte
  output for a full receipt; embedded weight and price barcode parsing, including the check digit.
- **Integration**, against real PostgreSQL through the real dispatcher — a sale rung up, tendered and
  completed relieves stock, raises exactly one financial event and produces a receipt; a sale whose
  stock is short still completes and marks the line `Refused` while the ledger stays untouched; a
  completed sale refuses further mutation; a park/resume round trip; a replayed sale id returns the
  first sale rather than creating a second; a till session's expected cash equals its cash tenders
  plus float, and the session refuses to close with a sale still open; a reprint is recorded as one.
- **Seed** — the demo tenant gets a terminal, an open till session and a completed sale, so
  `scripts/seed.sh` produces a store that has actually traded.

## Exit checklist

- [x] Five tables built, with the status machine, immutability and one-open-session-per-terminal
      rules enforced in the domain and again as database constraints
- [x] Migration applied and reversed against a real Postgres instance (`MigrationTests`, green)
- [x] RBAC permissions registered (nine); `PosModuleManifest` registered — the first non-core module
- [x] `pos.sale.tendered` raised through `IFinancialEventPoster`, with the no-rule path following
      ADR-070 rather than refusing the sale. Proved end to end by `scripts/seed.sh`: `JNL-000003`,
      Dr Bank 119.98 / Cr Sales 104.33 / Cr VAT 15.65, from the rule Stage 07 seeded
- [x] Stock relieved through Stage 08's `IStockLedgerPoster`, with the refusal path recorded on the
      line (ADR-073) rather than swallowed or escalated
- [x] `VumaRetail.Hardware` builds cross-platform, with the Windows-only transports named and
      deferred in `docs/HARDWARE.md` §1
- [x] `docs/HARDWARE.md` written; `docs/DATA_MODEL.md` §4g written; ADR-072 and ADR-073 appended
- [x] Seed data added — terminal, open till session, one completed sale with journal and stock movement
- [x] All 19 endpoints present in `/openapi/v1.json`, verified against a running host — re-verified
      independently by `stage-verifier` against a booted host, with summaries, examples and the
      `400/401/403/404/409/422/500` set on every one. The count read "20" until that run; it is 19
      operations across 17 paths (11 `MapPost` + 8 `MapGet`, a 1:1 match with the 11 commands and 8
      queries). Nothing was missing — the figure was a miscount, not a gap
- [x] Full suite green: unit 498/498, architecture 31/31, integration 306/306, 0 skipped
- [x] `docs/PROGRESS.md` updated, committed as `feat(stage-09): POS terminal & hardware`

**Not ticked, and deliberately so:**

- [ ] **The WPF till screen.** `VumaRetail.Desktop` cannot be built or run on this machine (ADR-031)
      and the design system it must consume is Stage 08b, NOT_STARTED. R3 is satisfied — the API it
      will render exists in full and is tested — but nobody can touch a till yet.
- [ ] **The two read-only carve-outs in `LICENSING.md`** — reprint under read-only, and the
      open-session carve-out that lets an in-flight sale and cash-up finish. Both need a fourth
      `ReadOnlyExemption` kind, which ADR-052 says needs an ADR and a `licence-safety` review. See
      `docs/PROGRESS.md` §4.10.
- [ ] **The five subagent reviews.** Launched, all five died (watchdog, then a session usage limit).
      Checks were done inline by the author instead. See the Stage 09 session-log entry — treat the
      architecture, money-and-tax and sync-and-offline reviews as `UNVERIFIED`, not `PASS`.

## Notes for later stages

- **Stage 08b (design system)** and the WPF shell are what turn this into a till somebody can touch.
  Every screen it needs is already an endpoint here.
- **Stage 10 (sales & promotions)** owns pricing and returns. `SaleLine.UnitPrice` becomes the
  *resolved* price rather than the supplied one, and a return is a new document that references a
  sale — neither changes this schema.
- **Stage 10b (accounts, lay-by, stokvels)** settles the `CustomerAccount` tender member this stage
  declares and leaves unhandled, and adds the lay-by document that reserves stock.
- **Stage 26 (workforce)** joins `TillSession` to a roster: the shift a cashier was scheduled for and
  the shift they actually banked are the same shape.
- **Stage 31 (packaging)** ships the Windows device transports — serial and USB printers, the OPOS
  scanner path, the real payment terminal SDK — behind the interfaces this stage defines.
