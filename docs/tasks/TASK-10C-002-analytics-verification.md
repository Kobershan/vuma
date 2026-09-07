# Task

## Status

COMPLETE (2026-09-07)

## Stage

Stage 10c — Quotes, Invoices & Sales Analytics

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE, API, TESTING, VERIFICATION

## Objective

Implement sales analytics and complete Stage 10c verification. This task builds the read-model layer for company-scoped and group-level sales analytics, then verifies all acceptance criteria against real PostgreSQL.

## Why

Without analytics, management has no visibility into sales performance per company, category, or channel. The group-level view is essential for multi-company operations but must never block trade (ADR-119).

## Scope

### Domain

- **`SalesAnalytics` entity**: Company-scoped daily/weekly/monthly/year-to-date aggregations. Dimensions: company, product category, sales channel, period. Carries `Revenue`, `CostOfSale`, `Margin`, `TaxLiability`, `PromotionEffectiveness` metrics.
- **`AnalyticsPeriod` enum**: Daily, Weekly, Monthly, YearToDate.
- **`AnalyticsRepository` port**: `GetByCompanyAsync`, `GetGroupAsync`, `RebuildAsync`.
- **Domain exceptions**: `AnalyticsRuleException`.

### Application

- **Commands**: `RebuildAnalyticsCommand` (triggers a full rebuild of read models).
- **Queries**: `GetSalesAnalyticsQuery`, `GetGroupAnalyticsQuery`.
- **Services**: `AnalyticsService` — computes aggregations, exposes stale read models with `AsAt`.
- **`ISalesAnalyticsQueryService` port**: Distinct return types for company vs group queries.

### Infrastructure

- **`registry.analytics_projections`** table configuration.
- **Analytics computation service** — scheduled or on-demand rebuild.
- **DI wiring**.

### Verification

- Re-run all acceptance criteria against real PostgreSQL.
- Coverage ≥ 80% on Domain + Application.
- Migration `Down` executed on scratch DB.
- Specialist review pass.
- Update `docs/DATA_MODEL.md`, `docs/PROGRESS.md`, `docs/CURRENT.md`.

## Out of Scope

- WPF UI for analytics dashboards (Stage 08b + Desktop).
- Android app analytics screen (Stage 30).
- Real-time analytics that could block trade.

## Architecture

- **ADR-119**: Group analytics are stale by design. They carry `AsAt` and disclose stale contributors. They never drive a commit or document generation decision.
- **ADR-106**: Consolidated output is labelled "Consolidated — management information, not a statutory statement".
- **§7 rule 19**: Analytics never block trade. A delay in group-level aggregation must never cause a timeout or failure at the till or in the order fulfilment pipeline.
- Group analytics are read-only projections, never sources of truth.

## Acceptance Criteria

1. Company-scoped analytics return daily, weekly, monthly, and YTD aggregations per company, category, and channel.
2. Group-level analytics return successfully even when the Stage 06d saga coordinator is lagging behind company databases (stale by design, ADR-119).
3. Every group figure carries `AsAt` and discloses stale contributors.
4. Coverage ≥ 80% on Domain + Application.
5. All Stage 10c acceptance criteria pass on real PostgreSQL.

## Definition of Done

- [x] All Stage 10c acceptance criteria PASS on real PostgreSQL (exit checklist in the stage document)
- [x] Coverage ≥ 80% measured on the stage's Domain + Application (92.1% union line coverage)
- [x] Migration `Down` executed on scratch DB, diff empty (re-applied cleanly afterwards)
- [x] `money-and-tax`, `multi-company-guard`, `architecture-guard` findings closed or recorded with reasons (structured review pass recorded below; arch suites green)
- [x] `docs/DATA_MODEL.md` §4h extended; replication registry updated (`docs/SYNC_AND_BACKUP.md` §3)
- [x] `docs/PROGRESS.md` + `docs/CURRENT.md` updated; committed and pushed

## Review pass (2026-09-07 — subagent runtime unavailable; structured checklist review)

- **money-and-tax:** price snapshots resolve-once (reprice-stability test); tax stored per line, never recomputed (ADR-075/138); single rounding at capture; one currency per document; `sales.invoice.posted` carries Net/Tax/Gross with no account fields (rule 12); missing rule warns, never blocks (R1/ADR-070); no threshold gating (no `IApprovalService` surface). PASS.
- **multi-company-guard (§11):** no handler touches two databases (arch suites green); legs write one company each with per-company numbering; replay by idempotency key; compensation by credit note (new document); no group read model feeds a commit; `AsAt` + stale disclosure on every group figure; split reconciles line-for-line/cent-for-cent (test asserts 10000/1500/11500); `SharedSourcing` links checked at point of use. One known limitation recorded: crash-between-legs recovery is manual (leg→document reference needs 06d schema). PASS with that follow-up.
- **architecture-guard:** layering intact; no cross-schema FK; `[CommandSideEffect]` on all 13 commands; `[Replicated]` on all 5 entities; private EF ctors; exemption rows added with reasons (issuing saga = sourcing-saga standing). PASS.
- **licence-safety:** no new module, no new entitlement flag needed (`sales` flag + `RequireModule` on all groups); `[CommandSideEffect(Write)]` on all commands so the read-only interceptor covers them; exemption set untouched; telemetry untouched. PASS.
- **sync-and-offline:** quotes/invoices `StoreToCloud/StoreWins`, analytics `NodeLocal`; leg outbox capture asserted (≥4 rows); replication registry doc updated. PASS.

## Work Log

- 2026-09-07: Analytics layer completed (`SalesAnalytics` daily grain + in-memory rollups, real `RebuildAsync` from posted invoices, `RebuildAnalyticsCommand`, company/group queries, `registry.analytics.view` permission with in-handler gate).
- 2026-09-07: Verification executed: full suites green (1047 unit / 54 arch / 472 integration on throwaway PG :55432), union coverage 92.1%, migration Up/Down round-trip on scratch, seed run on scratch proving quote→invoice→analytics with pack sizes and journal.
- 2026-09-07: Docs updated (`DATA_MODEL.md` §4h, `SYNC_AND_BACKUP.md` §3, `DECISIONS.md` ADR-141/142, `PROGRESS.md`, `CURRENT.md`); stage document exit checklist ticked.
