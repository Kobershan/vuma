# TASK-14B-001 — Field-sales proposals and approval application

**Status:** IN_PROGRESS · **Stage:** 14b · **Type:** Build (domain + application + unit tests)
**Depends on:** 14 (orders), 10c (invoices), 08c (availability/reservations/sourcing), 05
(`IApprovalService`, real `ApprovalEngine`), 06d (credit holds) — all present and verified.
**Reference reading:** `docs/stages/STAGE-14b-field-sales.md`, `docs/FIELD_SALES.md` in full,
ADR-107, ADR-108, ADR-109, ADR-110, ADR-112.

## Objective

Reps capture pro forma orders and credit notes that post nothing and reserve nothing; management
approves through Stage 05's engine; approval alone creates the real document. This task owns the
domain aggregate, the application handlers up to the saga boundary, the availability/performance
read ports, and unit tests. The approval saga, persistence, API and verification are TASK-14B-002.

## Scope

### Domain — `src/VumaRetail.Domain/FieldSales/`

- `Rep.cs` — `RegistryUserId` (bare uuid into registry users), `DisplayName`, `CompanyIds`
  (may sell for), `CustomerIds` (territory customers), `TerritoryProvince/City/Suburb`
  (optional geo territory, same hierarchy as 13b waves), `SeeCost` (visibility profile),
  `IsActive`. Methods: `AssignTerritory`, `GrantCompany`, `SetVisibility`, `Deactivate`.
- `ProFormaOrder.cs` — `ProFormaNumber` (`PF-`), `RepId`, `CompanyId` (ordering company),
  `PartnerId` (customer, bare uuid), `Currency`, `Status` (`Draft/Submitted/Approved/
  Amended/Rejected/Expired/Converted`), price/promotion snapshots per line, availability
  `AsAt`, `ExpiresAt` (default +7d), `ApprovalRequestId?`, `ConvertedOrderId?`,
  `RepriceDelta?`. Methods: `Submit`, `RecordApproval(requestId)`, `MarkConverted(orderId)`,
  `Reject(reason)`, `Amend` (→ Amended, editable again via new version? No — amend returns to
  Draft with reason kept; history is the status trail), `Withdraw`, `Expire`.
  Totals derive from lines. No postings, no reservations — asserted structurally (no finance/
  inventory references in this project beyond ports).
- `ProFormaOrderLine.cs` — item/variant (exactly one), qty/uom, quoted unit price, discount,
  tax code/amount, net, pack snapshot, price-list id, promo ids, line availability snapshot.
- `ProFormaCreditNote.cs` (+lines) — `PFC-` series, original invoice id + company, reason code,
  same status set (minus Converted → `Applied` with `ResultingReturnId`).
- `RepTarget.cs` — rep, company, period (month), target net, version; `Supersede` creates a new
  version, never edits.
- `RepPerformanceSnapshot.cs` — rep, company (or group roll-up marker), period, figures
  (captured count/value, approved/converted value, rejected/expired counts, invoiced/credited/net
  values, margin where visible, active/new customers), `Snapshot(created, version, reason)`,
  immutable after creation (Scorecard pattern, ADR-110).
- `FieldSalesExceptions.cs` — coded: `PROFORMA_NOT_SELLABLE` (territory/company refusal),
  `PROFORMA_EXPIRED`, `PROFORMA_ILLEGAL_TRANSITION`, `PROFORMA_NEEDS_REPRICE`,
  `REP_FORBIDDEN` (403-style, names the missing scope).

### Application — `src/VumaRetail.Application/FieldSales/`

- Ports (`Abstractions/FieldSales/FieldSalesPorts.cs`): `IProFormaRepository`
  (Find, FindByNumber, FindByIdempotencyKey, Add), `IRepRepository` (Find, Add),
  `IRepAvailabilityQuery` (group available per company with AsAt, filtered by rep),
  `IRepPerformanceService` (SnapshotPeriod, QueryPeriod with comparison + variance),
  `IFieldSalesApprovalService` (ApproveAsync → runs the TASK-14B-002 saga; declared here).
- Commands: `CaptureProFormaCommand` (rep, company, customer, lines with caller prices +
  pack/tax snapshots, idempotency key → replay returns existing), `SubmitProFormaCommand`
  (→ EvaluateAsync with default policy → Pending, stores RequestId; MayProceed (no policy)
  → runs approval inline), `AmendProFormaCommand`, `WithdrawProFormaCommand`,
  `ApproveProFormaCommand` (manager: DecideAsync(Approve) → approval service),
  `RejectProFormaCommand` (DecideAsync(Reject) → Rejected + reason),
  `ExpireProFormasCommand` (hosted service), credit-note mirrors
  (`CaptureProFormaCreditNoteCommand`, `ApproveProFormaCreditNoteCommand` → sales return).
- Queries: `GetRepAvailabilityQuery` (rep, item → per-company available + AsAt; refuses
  customers/companies outside territory with REP_FORBIDDEN), `GetRepPerformanceQuery`
  (rep, period, compareTo → both + variance; own vs team permission enforced by caller role:
  handler takes CallerRepId + CallerCanViewTeam, refuses cross-rep reads without it),
  `GetProFormaQuery`.
- Permissions + manifest in `Permissions/FieldSalesPermissions.cs`: `fieldsales.proforma.capture`,
  `.submit`, `.view`, `.approve` (high-risk), `fieldsales.performance.own`, `.team`,
  `fieldsales.cost.view`; manifest module `field-sales`, flag `field-sales`, non-core.
- Rule 13: no approval logic here — only `EvaluateAsync`/`DecideAsync` calls (asserted by
  architecture test addition in 002: every `*Approve*` command calls `IApprovalService`).
- Rule 20: handlers touch one company DB max (pro forma tables live in the ORDERING company's
  DB? or registry?). DECISION (ADR-148, appended in 002): pro formas live in the ordering
  company's `fieldsales` schema (tenant's rep data follows the selling company; group reads go
  through the performance roll-up). Handlers resolve one company context only.

## Out of Scope

Approval saga legs, EF/migration, endpoints, hosted services, seed, integration tests (002).
No invoice/credit-note documents of our own (Stage 10 paths). No Android/desktop UI.

## Acceptance (unit-provable here)

- Capture posts nothing/reserves nothing: handler test asserts zero journal/receipt/reservation
  writes (repository substitutes record every call — assert only pro forma repo touched).
- Offline replay twice → one pro forma (idempotency key).
- Expired submit → refused with NEEDS_REPRICE code path (approve path in 002).
- Territory refusal: rep A reads rep B's customer → REP_FORBIDDEN.
- Performance comparison math: Aug vs Jul + variance, re-run same figures.
- Coverage ≥ 80% new Domain + Application.
