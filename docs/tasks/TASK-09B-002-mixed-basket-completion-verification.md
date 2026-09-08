# TASK-09B-002 — Mixed-basket completion saga, API, and verification

**Status:** COMPLETE (2026-09-08) · **Stage:** 09b · **Type:** Build (infrastructure + API + verification)
**Depends on:** TASK-09B-001 (session aggregate, handlers, allocator, permissions).
**Reference reading:** TASK-09B-001; `docs/stages/STAGE-09b-mixed-basket.md` (Completion,
Documents, API, Tests/acceptance, Exit checklist); TRADING_GROUP.md §4 rules 4–10;
ADR-102, ADR-103, ADR-104, ADR-105, ADR-112, ADR-116, ADR-126; ADR-145 (appended here);
`src/VumaRetail.Infrastructure/Sales/InvoiceIssuingService.cs` (the leg pattern to copy);
`src/VumaRetail.Infrastructure/Inventory/SourcingCommitService.cs` (reservation legs).

## Objective

At the end of this task one till completes one mixed basket into one posted tax invoice
per company (each in its own database, each with its own VAT number and `INV` sequence),
one fully-allocated receipt per segment, per-company till sessions for cash-up, a
non-fiscal basket summary, and 9 permission-gated endpoints — with all-or-none saga
semantics, idempotent replay, and the operator's example reproducible on seed.

## Why

This is the first place one human action writes two databases (stage Objective). The
saga shape is settled (ADR-116) and twice precedented (08c sourcing commit, 10c invoice
issue drive `SagaIntent`/`SagaLeg` rows directly because `DispatchLegAsync` is a
documented no-op pending 07C-004). This task copies that shape a third time and records
the convergence point, rather than waiting on 07c or inventing a fourth shape.

## Scope

### Infrastructure — `src/VumaRetail.Infrastructure/`

- `Persistence/Configurations/Registry/TradingSessionConfigurations.cs`:
  `TradingSessionConfiguration` (table `trading_sessions`, schema `registry`,
  unique `ux_trading_sessions_tenant_idempotency` on `(tenant_id, idempotency_key)`
  filtered `deleted_at IS NULL`, unique session-number index), segment/line/tender
  configurations; `HasMoney` for all amounts; scalar quantity columns
  (`quantity_value numeric(18,6)` + `quantity_uom varchar(16)`, NOT `HasQuantity`
  with `new Quantity(...)` — throws at model build, per 10b precedent).
- `Persistence/VumaRegistryDbContext.cs` (extend): `DbSet<TradingSession>` (+
  segments/lines). Configs applied in `OnModelCreating` where registry configs live.
- `Registry/TradingSessionRepository.cs`: `ITradingSessionRepository` EF
  implementation (`async`/`await` + `.ConfigureAwait(false)`).
- `Registry/MixedBasketCompletionService.cs` (`IMixedBasketCompletionService`,
  port in `Application/Abstractions/Registry/`): intent type
  `"trading-session-complete"`, idempotency key = session key. `CompleteAsync`
  body calls `RequireLink(sessionCompany, segmentCompany, SharedTill)` for every
  sister segment FIRST (links lapse between scan and payment — TRADING_GROUP.md §2),
  checks the ordering company's Operator ID (ADR-121), writes the intent + one leg
  per segment, then executes legs in company order. Single-segment sessions take NO
  registry path (rule 12): one direct leg, no intent row — asserted by test.
  Replay of a completed session returns stored invoice numbers without touching
  company DBs. The ONE `.CreateAsync` call site in this file (arch-test counted).
- Per-segment leg, inside that company's own database, one serialisable transaction:
  1. ensure an open till session for (terminal, company) — open a zero-float
     session as the cashier when none is open (ADR-145; gives per-company cash-up);
  2. reserve + consume stock through `IReservationService` under the session key
     (ADR-102/ADR-103; shortfall fails the leg — never negative);
  3. `Sale.Open` + `SaleLine.Ring` + `SaleTender.Capture` (segment allocation as
     the tender) + `ISaleCompletionService.CompleteAsync` (the single stock issue
     set + `pos.sale.tendered` event);
  4. `Invoice.Create` + `AddLine` (pack snapshots from the session lines) + `Post`
     + `IInvoiceFinancialEventPublisher.PublishAsync` (per-company `INV` number);
  5. `ArReceipt.RecordFromGroup` fully allocated against that invoice
     (`GroupDocumentId` = session id, `IntentId` = saga intent) + receipt financial
     event through `IFinancialEventPoster` (posting rules decide accounts — §7
     rule 12);
  6. outbox capture for every new row (same `CompanyOutboxCapture` shape as
     `InvoiceIssuingService`); `AuditStamper.Stamp`; save; commit.
- Compensation (reverse leg order): void the sale (reason names the session —
  reversal document 1, "no sale stands"), record a reversal `ArReceipt` (negative
  amount/allocations against the same invoice — domain-legal per `Money`
  docs — reversal document 2), release open holds under the session key,
  post a `trading.session.leg-reversed` event (missing rule logs per ADR-070).
  Posted invoices cannot auto-credit (Stage 10's invoice credit note does not
  exist): the session records them in `UnwoundInvoiceIds` and returns to
  `Tendered` with the reason naming them (ADR-145). Never deletes, never edits.
- `CompleteTradingSessionCommandHandler` (application, thin): loads session,
  delegates to the service. Returns invoice numbers per company.
- `ReturnMixedBasketLineService` (application service, per-leg child scopes via
  the same gateway): return splits by origin — one `CreateSalesReturnCommand`
  per origin company against that company's own sale (ADR-128); a request naming
  another company's invoice is refused with `TRADING_RETURN_WRONG_COMPANY`
  naming both invoice numbers. No cross-company credit note, ever.
- EF registry migration `Stage09b_TradingSessions` (reversible `Down` executed on
  scratch DB). `docs/SYNC_AND_BACKUP.md`: register `registry.trading_sessions`
  (+ segments, lines) with the correct direction (registry-held coordination
  record — follow the `SagaIntent` row's precedent).
- `TradingGroupGuardTests.cs`: add
  `("src/VumaRetail.Infrastructure/Registry/MixedBasketCompletionService.cs",
  "CompleteAsync", "SharedTill")`.

### Contracts + Web

- `src/VumaRetail.Contracts/TradingSessions/TradingSessionContracts.cs`: open/add/
  void-line/get/tender/override/complete/void/documents request-response DTOs.
- `src/VumaRetail.Web/TradingSessions/TradingSessionEndpoints.cs`: the 9 routes
  from the stage doc, permission-gated (`basket.mixed` writes,
  `basket.allocate.override` override, `basket.void` voids; reads behind
  `basket.mixed`), present in `/openapi/v1.json`. One `MapTradingSessions()` line
  in `StoreServer/Program.cs`.

### Documents — `src/VumaRetail.Reporting/Documents/`

- Per-company tax invoice reuses 10c (assert VAT number + sequence are the
  company's — test, not new code).
- `BasketSummaryDocument`: both invoice numbers, per-company subtotals, one total,
  and the sentence **"This is not a tax invoice. Your tax invoices are {a} and
  {b}."** in body-size type; never a VAT summary (asserted by test on the model +
  rendered output).
- 80mm receipt: both invoices in sequence then the summary block (extend the
  receipt builder; one artefact per company + one combined block, never a merged
  VAT line).

### Seed — `src/VumaRetail.StoreServer/DemoSeed.cs` (extend)

The operator's example end to end: Operator ID + Siyaya/Noortgats companies +
`SharedTill` link + routing rows (hot plate, gloves → Noortgats; maize → Siyaya) +
session TS with 2 + 3 + 1 lines, tendered, completed. Proof line quotes both
invoice numbers; both numbers quoted in `docs/PROGRESS.md`.

## Out of Scope

Till UI (`UNVERIFIED — needs Windows`). Link management UI (06e). New payment
types, new invoice/credit-note documents. Fixing 07C-004 (convergence recorded,
not executed). Touching POS/10c/08c handlers.

## Architecture

Copy `InvoiceIssuingService` leg-for-leg (idempotent intent → link checks →
per-company serialisable legs in company order → ack → complete; failure →
compensate + `Tendered`-with-reason). The completion service touches only the
registry itself; legs run in child scopes (ADR-116). Entitlement: `MultiCompany`
flag + `SharedTill` scope check; metering counters (mixed sessions/day, segments/
session — counts only).

## Architectural Boundaries

- §7 rule 20: single `.CreateAsync` site (this service file); handlers open zero.
- §7 rule 12: posting rules decide accounts; events carry named amounts only.
- ADR-122: `RequireLink(..., SharedTill)` at scan (001) AND at completion (this
  task) — the test row covers the completion body.
- ADR-125: tax per segment; basket total = sum of rounded segments (test names
  the wrong alternative).
- R10: metering counts only.

## Dependencies

TASK-09B-001. Seed needs routing rows + link rows (06e/06d seed helpers as they
exist; else insert directly in DemoSeed with a comment).

## Relevant Files

`InvoiceIssuingService.cs` (copy), `SourcingCommitService.cs` (reservation legs),
`SaleCompletionService.cs`, `GroupReceiptLegHandler.cs` (receipt-posting shape —
note its empty-allocations bug; ours allocates in full),
`tests/VumaRetail.IntegrationTests/Sales/SalesDocumentsHarness.cs` (two-company
harness pattern), `RegistryMigrations/` (live registry migration dir).

## Relevant Documentation

Stage doc in full; ADR-145 (below, appended with this task).

## Implementation Requirements

1. Warnings-as-errors clean (Domain/Application).
2. `IClock` everywhere; no wall-clock reads.
3. Replay completion 3× → same 2 invoice numbers, exactly 2 invoices + 2 sales +
   2 receipts in the company DBs (count-asserted).
4. Offline replay: same idempotency key through the path twice → same result
   (rule 8 — no bespoke endpoint).
5. Voided session: every hold released, zero journals (assert journal absence per
   company + reservation release).

## Data/Database Impact

Registry migration (additive, reversible). No company-DB schema change (uses
existing pos/sales/finance/inventory tables through existing configs).

## API Impact

9 routes, OpenAPI presence test, ProblemDetails on refusals (link missing names
both companies + `SharedTill`; wrong-company return names both invoices).

## Security

Endpoint permission gates; idempotency keys are opaque caller UUIDs (no
enumeration — `FindByIdempotencyKeyAsync` scoped to tenant).

## Multi-Company/Tenant Impact

First `SharedTill` enforcement at completion; per-company till sessions;
`company_id` on session/segment/line rows; Operator ID check mirrors
`InvoiceIssuingService`.

## Sync/Offline Impact

New registry tables registered in `SYNC_AND_BACKUP.md`; legs capture outbox rows
so segments replicate StoreToCloud per company like any sale/invoice/receipt.

## Acceptance Criteria

Every test in the stage doc's Tests/acceptance section, using the operator's
numbers (2 × R799.00 hot plate, 3 × R100.33 gloves, 1 × R214.00 maize, VAT 15%
inclusive; R2 113.99-style allocation determinism). Plus: migration `Down`
executed; coverage ≥ 80% on the stage's Domain + Application; seed proof line
with both invoice numbers.

## Tests Required

Unit (001's, extended if needed) + new integration:
`MixedBasketCompletionTests` (two invoices/two DBs/VAT-per-segment, rounding-sum,
allocation determinism, all-or-none compensation, replay, offline replay,
origin-company returns, cross-company refusal, single-company no-registry,
void releases). Web presence tests (401/403 + OpenAPI). `DATA_MODEL.md` §4l.

## Edge Cases

- Link suspended between scan and payment → completion refuses naming scope.
- Lapsed (read-only) sister company → its leg refuses, others untouched, reason
  names the lapse.
- Till session closed mid-basket in one company → leg re-ensures (opens) one;
  never reuses a closed session.
- Cashier override making one segment zero (full discount?) — allowed if exact;
  zero-amount segment still invoices (R0 invoice is a real document).

## Definition of Done

- `dotnet build -c Release`: 0 errors, 0 warnings Domain/Application.
- `dotnet test`: unit + integration green; arch no worse than baseline + new
  guard row green; coverage ≥ 80% stage Domain+Application.
- Migration `Down` executed + re-applied; `has-pending-model-changes` clean both
  contexts.
- Seed exit 0 with proof line; OpenAPI lists all 9 routes.
- Stage doc statuses → COMPLETE; `CURRENT.md` + `PROGRESS.md` (+ §4l, + ADR-145);
  branch merged to `main`, pushed, CI jobs green except the 4 known 08b failures.

## Follow-up Findings

- (filled during execution)

## Work Log

- 2026-09-08: task written alongside 001; 07C-004 convergence point recorded
  (legs execute inline like 08c/10c; converge when the dispatcher lands).
