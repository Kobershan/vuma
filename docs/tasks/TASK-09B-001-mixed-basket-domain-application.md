# TASK-09B-001 — Mixed-basket domain and application

**Status:** COMPLETE (2026-09-08) · **Stage:** 09b · **Type:** Build (domain + application + unit tests)
**Depends on:** Stage 09 audit (done 2026-09-08 — §4.10–§4.15 fixes present, POS 332 unit + 76 integration green);
06e (`ICompanyLinkService.RequireLink` + `SharedTill` scope), 06d (`SagaIntent`/`SagaLeg`),
07c (`ArReceipt.RecordFromGroup`), 08c (`IReservationService`), 10c (`Invoice` aggregate +
`IInvoiceFinancialEventPublisher`) — all present in code.

**Reference reading:** `docs/stages/STAGE-09b-mixed-basket.md` (Objective, Business rules 1–12);
`docs/TRADING_GROUP.md` §4; ADR-100, ADR-116, ADR-121, ADR-122, ADR-125, ADR-126, ADR-128;
`docs/ARCHITECTURE.md` (boundaries); `docs/TESTING.md` §3.

## Objective

At the end of this task the mixed basket exists as a registry domain aggregate with its
application handlers: open a trading session at a shared till, add scanned lines (company
resolved from the routing index, `SharedTill` link refused at scan time), void lines,
capture one tender, allocate it cent-exact across segments, and read the session with
per-segment tax. Completion *orchestration* is TASK-09B-002 — this task owns everything
up to the saga boundary, plus the pure allocation math both tasks share.

## Why

One scan flow, two sets of books (R13). The cashier never thinks about companies; the
routing index decides, the link guard refuses early, and each segment prices, taxes and
rounds inside its own company (ADR-125). Link refusal at payment time is a till that took
money it cannot account for — so the refusal lives in the line handler.

## Scope

### Domain — `src/VumaRetail.Domain/Registry/Trading/`

- `TradingSession.cs` — aggregate: `SessionNumber` (series `TS`, caller-minted via
  `IDocumentNumberSequence`-shaped string passed in), `PremisesId`, `TerminalId`,
  `CashierUserId`, `SessionCompanyId` (the till's own company), optional
  `CustomerGroupPartnerId` (bare uuid), `Currency`, `Status` (`TradingSessionStatus`:
  `Open = 0, Tendered = 1, Completing = 2, Completed = 3, Voided = 4,
  CompletionFailed = 5`), `IdempotencyKey` (unique per tenant), `Tender*` (nullable
  until captured), `FailureReason` (set on compensation return), `CompletedAt`,
  `ResultSummary` (invoice numbers per company, set on completion). Methods:
  `Open`, `AddLine` (creates the company segment on first line for that company),
  `VoidLine`, `CaptureTender`, `OverrideAllocation`, `MarkCompleting`, `MarkCompleted`,
  `MarkCompletionFailed(reason)`, `Void(reason, now)`. No balance is stored — segment
  totals derive from lines.
- `TradingSessionSegment.cs` — `CompanyId`, lines, `TenderAllocation` (Money, set at
  tender time), `ResultingInvoiceId`/`ResultingInvoiceNumber`/`ResultingSaleId`
  (set on completion), `SegmentStatus` (`Building = 0, Tendered = 1, Posted = 2,
  Compensated = 3`). Totals (`Net`/`Tax`/`Gross`) computed from lines, rounded per
  segment (AwayFromZero, 2dp — the same rule `Money.RoundToCurrencyScale` applies).
- `TradingSessionLine.cs` — `SegmentId`, `Barcode`, `ItemId`/`ItemVariantId`
  (exactly one), `Description` snapshot, `QuantityValue`/`QuantityUom`,
  `UnitPrice`/`DiscountAmount`/`TaxCode`/`TaxAmount`/`Net` (`Money`, priced snapshots),
  `PackSizeDescription`, `PriceListId`, `CompanyId` (resolved, denormalised for the
  leg query), void flag.
- `TenderAllocator.cs` (static domain service) — proportional by segment gross,
  cent-exact, remainder dust deterministically to the largest segment (ties: lowest
  company id first — byte-order stable), override path re-validating exact sum.
  States its rule on the result (`Basis` string, e.g. `proportional, dust R0.01 to
  <company>`).
- `TradingSessionStatus.cs`, `TradingSessionExceptions.cs` (coded factories:
  `TRADING_LINK_REQUIRED`, `TRADING_TENDER_NOT_COVERED`, `TRADING_ALLOCATION_MISMATCH`,
  `TRADING_ILLEGAL_TRANSITION`, `TRADING_STALE_SESSION`, `TRADING_RETURN_WRONG_COMPANY`).

### Application — `src/VumaRetail.Application/Registry/Trading/`

- `Abstractions/TradingSessionPorts.cs` (extend `Application/Abstractions/Registry/`):
  `ITradingSessionRepository` (`FindAsync`, `FindByNumberAsync`,
  `FindByIdempotencyKeyAsync`, `Add`, `Update`, `ListOpenAsync`).
- `Commands/`: `OpenTradingSessionCommand(PremisesId, TerminalId, CashierUserId,
  SessionCompanyId, Currency, CustomerGroupPartnerId?, IdempotencyKey)` (idempotent
  replay by key returns the existing session);
  `AddBasketLineCommand(SessionId, Barcode, QuantityValue, QuantityUom, UnitPrice,
  Currency, DiscountAmount = 0, LineId? = null)` — resolves barcode via
  `IBarcodeResolver`, takes the first candidate (ADR-100 collision order is the
  resolver's), calls `ICompanyLinkService.RequireLink(sessionCompany, lineCompany,
  SharedTill)` for a sister company (same-company lines skip the registry link read
  only in the sense that a link to self is trivially satisfied — still call
  `RequireLink` uniformly; it must succeed for identical companies), snapshots pack
  size via `IPackSizeResolver`, prices tax via `ITaxCalculator`, appends to the
  company's segment; replay-safe by `LineId` (same shape as §4.11);
  `VoidBasketLineCommand(SessionId, LineId)`; `CaptureTenderCommand(SessionId,
  TenderType, Amount, Currency, Reference?, TenderId? = null)` (amount must cover the
  basket gross; computes default proportional allocation via `TenderAllocator`);
  `OverrideTenderAllocationCommand(SessionId, Allocations)` (must sum to tender
  exactly);   `VoidTradingSessionCommand(SessionId, Reason)` (state transition only: holds are
  taken inside completion legs, never at scan time, so a pre-completion void holds
  nothing to release — the invariant is proven by 002's void-releases test).
- `Queries/`: `GetTradingSessionQuery(SessionId)` (segments, per-segment tax,
  allocation, invoices when completed); `GetSessionDocumentsQuery(SessionId)`
  (invoice ids + numbers per company + summary model with the not-a-tax-invoice
  sentence — the sentence itself is asserted here, rendering is 002).
- `Permissions/TradingSessionPermissions.cs`: `basket.mixed`
  (`trading.basket.mixed`, high-risk), `basket.allocate.override`
  (`trading.basket.override`), `basket.void` (`trading.basket.void`);
  `TradingModuleManifest` (module id `trading-sessions`, licence flag
  `multicompany` — the stage doc's `MultiCompany` flag; non-core).
- Every handler: one company context max (§7 rule 20). Handlers touch the registry
  session + resolver ports only; no handler opens a company database (legs are 002's
  service, mirroring `InvoiceIssuingService`'s remarks).

## Out of Scope

Completion saga + legs, EF/migrations, endpoints, reporting documents, seed,
integration tests (all TASK-09B-002). No new invoice/credit-note document. No till UI.
No changes to POS handlers.

## Architecture

Follows the 08c/10b/10c shape: registry-owned aggregate, command handlers, ports in
`Application/Abstractions/Registry/`, permissions + manifest. Pricing snapshots reuse
Stage 10's `IPriceResolver`? No — lines carry caller-supplied prices like Stage 09
(ADR-072); tax via Stage 07's `ITaxCalculator`, pack size via 10c's
`IPackSizeResolver`. Segment pricing inside the owning company happens in 002's legs;
this task's `AddBasketLine` resolves tax/pack snapshots through the same ports in the
session scope (snapshot-at-capture, ADR-112/ADR-138 — never recomputed downstream).

## Architectural Boundaries

- §7 rule 12: no GL account, no amounts outside event-shaped records (no new
  financial events in this task).
- §7 rule 20: no handler opens a company database. `MultiCompanyGuardTests` counts
  `.CreateAsync` per file — this task adds none.
- ADR-122: `RequireLink(..., SharedTill)` in `AddBasketLineCommandHandler` at scan
  time (TRADING_GROUP.md §2 checklist). The 002 completion service re-checks before
  writing (links can lapse between scan and payment — TRADING_GROUP.md §2: lapsed
  company drops out for writes).

## Dependencies

Stage 09 (till gy — audit green), 06e links, routing index (`IBarcodeResolver`),
`ITaxCalculator`, `IPackSizeResolver`, `IReservationService` (release on void),
`IApprovalService`? No — no approval in scope (basket has no threshold gate).

## Relevant Files

`src/VumaRetail.Domain/Registry/GroupEntities.cs` (link scope, routing entry);
`src/VumaRetail.Application/Abstractions/Registry/CompanyLinkService.cs`;
`src/VumaRetail.Domain/Pos/Sale.cs` (rounding/totals precedent);
`src/VumaRetail.Domain/Primitives/Money.cs` (AwayFromZero, 2dp);
`tests/VumaRetail.ArchitectureTests/TradingGroupGuardTests.cs` (row lands in 002
with the completion service).

## Relevant Documentation

Stage doc + TRADING_GROUP.md §4 + ADRs above. ADR-145 (new, appended in 002)
records the leg/till-session/compensation decisions.

## Implementation Requirements

1. Nullable + warnings-as-errors clean in Domain/Application.
2. `IClock` for all time; no `DateTimeOffset.UtcNow`.
3. Idempotency keys: session open + line add + tender capture replay-safe.
4. Same-company baskets work with zero link rows (RequireLink on identical
   companies must succeed — verify against `CompanyLinkService.RequireLink`
   behaviour; if it refuses self-links, short-circuit same-company BEFORE the call
   with a comment citing this task).
5. Tender must cover gross; change is NOT given at session level (each leg's sale
   handles its own balance: allocation equals segment gross exactly, so no change).

## Data/Database Impact

None in this task (entities + `EntityConfiguration<T>` shape prepared for 002;
no migration).

## API Impact

None in this task (DTOs + endpoints are 002).

## Security

Permission strings declared; enforcement in endpoints (002). Visibility: sessions
are till-scoped; no cross-tenant reads (tenant filter via base entity).

## Multi-Company/Tenant Impact

First caller of `SharedTill`. `company_id` on every new table (002 migration).

## Sync/Offline Impact

Offline capture replays through `POST /api/v1/sync/batches` (no bespoke endpoint —
rule 8); session `IdempotencyKey` is the dedupe key. Proven by 002's replay test.

## Acceptance Criteria

- `Tender_allocation_is_cent_exact_and_deterministic` logic at domain level
  (R2 113.99-style split, dust to largest, byte-stable ties).
- `Basket_total_is_the_sum_of_rounded_segments` (segment rounding fixture where
  rounding-the-sum differs; wrong alternative named in a comment).
- `Unlinked_company_line_is_refused_at_scan_time` (handler test with stubbed
  resolver + link service refusing; basket keeps one segment).
- Same-company session completes the build path with no link row (link-service
  substitute with zero links configured).
- Illegal transitions refused (tender on completed, line on tendered, complete
  before tender, void after completion).

## Tests Required

Unit (xUnit): `TenderAllocatorTests`, `TradingSessionTests` (status machine,
totals, allocation override validation), `AddBasketLineHandlerTests` (refusal,
snapshot, idempotent replay), `CaptureTenderHandlerTests` (under-tender refusal,
default allocation). Coverage ≥ 80% on the new Domain + Application.

## Edge Cases

- Barcode resolving to multiple companies (take resolver order; note candidate).
- Retired barcode → resolver returns none → coded refusal.
- Override allocation with rounding dust manipulated by cashier (accept any exact
  split; proportional is only the default).
- Currency mismatch between line and session (refuse — §4.13's lesson).
- Zero-line tender (refuse — a sale with no lines does not complete).

## Definition of Done

- `dotnet build -c Release`: 0 warnings Domain/Application.
- New unit tests green; stage coverage ≥ 80%.
- No architecture-test regression (full arch suite no worse than 72/76 baseline).
- No migration (none needed); task file + PROGRESS updated.

## Follow-up Findings

- (filled during execution)

## Work Log

- 2026-09-08: task written (planning gate 09b-MAP-01 discharged in miniature —
  this + 002 are the independently executable units).
