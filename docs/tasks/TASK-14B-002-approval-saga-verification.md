# TASK-14B-002 — Field-sales approval saga, API and verification

**Status:** COMPLETE · **Stage:** 14b · **Type:** Build (infrastructure + API + verification)
**Depends on:** TASK-14B-001. **Reference reading:** TASK-14B-001; FIELD_SALES.md §3–§5;
ADR-101, ADR-102, ADR-108, ADR-116; `InvoiceIssuingService`/`MixedBasketCompletionService`
leg patterns; `SupplierScorecard` snapshot pattern.

## Objective

Approval converts a pro forma into a real order (and_pro into invoices) through a resumable,
compensating saga; credit notes convert into Stage 10 sales returns; performance snapshots close
periods; the module is reachable over 10 permission-gated endpoints and proven on seed + tests.

## Scope

- Infrastructure: `fieldsales` schema + `Schemas.FieldSales`; EF configs + repos
  (`ProFormaRepository`, `RepRepository`); `FieldSalesApprovalService` (intent
  `field-sales-approval`, idempotency = pro forma key): reprice vs today (delta recorded),
  plan via `ISourcingPlanner`, credit hold via `IGroupCreditService` against the ordering
  company's group when one exists (no group → proceed, COD-style; refusal carries the group
  position), reserve legs per sourcing company (gateway, `ProFormaApproval` source), create +
  confirm `SalesOrder` in the ordering company, issue invoices via `IInvoiceIssuingService`,
  confirm hold. Compensate in reverse (releases, order cancel, hold release); crash resumes
  from the intent (legs idempotent by deterministic ids + existence checks, 09b shape).
  Credit-note approval: sales return in the origin invoice's company (no cross-company note).
- `ListGroupsForCompanyAsync` additive port on `IGroupCreditService` (lookup for the hold).
- Migration `Stage14b_FieldSales` (reversible, Down executed). `SYNC_AND_BACKUP.md` rows
  (`fieldsales.*` StoreToCloud/StoreWins).
- Web: `/api/v1/field-sales/pro-formas`, `/pro-forma-credit-notes`, `/approvals`,
  `/availability`, `/performance` — permission-gated, OpenAPI presence tests.
- Hosting: `ProFormaExpiryHostedService`, `RepPerformanceSnapshotHostedService` + DI.
- Seed: two reps + territories, three pro formas (approved→split invoice, rejected, expired),
  closed-period snapshot + proof line.
- Integration (real PG, two company DBs + registry like 09b harness): offline capture replay
  → one; approval with moved availability → delta + backorder remainder; exhausted group
  credit → refused with position, nothing reserved; crash-after-reserve resume → exactly once;
  cross-company approval → one order + two invoices reconciling; rejection → zero rows;
  territory 403s; performance Aug-vs-Jul + rerun stability.
- Docs: ADR-148 (pro formas live in ordering company's DB), DATA_MODEL §4o, PROGRESS,
  CURRENT, stage doc COMPLETE.

## Out of Scope

Till UI, Android app, commission (Stage 25/26), second pricing authority (Stage 10 owns),
transport for sync batches (existing path).

## Verification evidence

- 2026-09-10: real PostgreSQL Field Sales integration slice passed 9/9 via
  `dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --filter FullyQualifiedName~FieldSales`.
- 2026-09-10: Re-run of `FieldSalesApprovalTests` against the disposable local PostgreSQL server passed 9/9. This confirms the approval/replay, availability delta, credit refusal, crash-resume, cross-company, rejection, territory, performance, and credit-note scenarios remain green.
- Full integration regression was re-run on 2026-09-11: 568/568 passed against disposable PostgreSQL.
