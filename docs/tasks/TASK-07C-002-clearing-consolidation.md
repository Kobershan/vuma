# Task

## Status

NOT_STARTED

## Stage

Stage 07c — Cross-company money: group receipting, allocation, inter-company clearing, consolidated reporting

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE

## Objective

Implement inter-company clearing intents, consolidated reporting, the net-zero reconciliation job, period-close guard, posting-rule seeding, and clearing-account auto-creation.

## Why

When cash from a group receipt lands in one company's bank account and settles a sister company's invoice, both companies must post and they are two databases. Inter-company clearing keeps every company's books balanced on their own while netting to zero across the group. Consolidated reporting gives the group owner one labelled, read-only view. A period cannot close with an intent outstanding.

## Scope

- Domain: `InterCompanyClearingIntent` entity in `Domain/Registry/GroupReceiptEntities.cs`
- Application: `IConsolidationService` (trial balance, income statement, balance sheet, age analysis)
- Application: `ICompanyClearingAccountService` (auto-create clearing accounts per company pair)
- Application: Period-close guard that refuses over outstanding inter-company intents
- Infrastructure: `ConsolidationService` (fan-out read + assembly + watermark + AsAt + stale contributors)
- Infrastructure: `NetZeroReconciliationJob` (scheduled cross-DB reconciliation with alarm)
- Infrastructure: `ClearingAccountService` (auto-create clearing accounts from template)
- Infrastructure: Posting-rule seeding for `GroupReceiptAllocated`, `InterCompanyClearingRaised`, `InterCompanySettled`
- Infrastructure: Clearing-account template in chart of accounts seed
- Web: `/api/v1/reports/consolidated/*` endpoints (trial balance, income statement, balance sheet)
- Permissions: `group.report.consolidated`
- Tests: clearing intent settlement, net-zero assertion, consolidation assembly, period-close refusal

## Out of Scope

Group receipt capture and allocation (TASK-07C-001), API endpoints for group receipts (TASK-07C-001).

## Architecture

Layer: Domain (clearing intent entity), Application (consolidation service, clearing account service, period-close guard), Infrastructure (reconciliation job, posting-rule seed, EF configs), Web (consolidated report endpoints). Component: inter-company clearing and consolidated reporting. Data flow: allocation creates clearing pair → clearing intent in registry → each leg dispatched to company DB → reconciliation job verifies net-zero → consolidation service fans out period figures from each company → assembles labelled, watermarked read model in registry.

## Architectural Boundaries

- Clearing intent is immutable; settled only when both legs acknowledge (ADR-105)
- Net-zero is enforced by standing reconciliation, not transactional assertion (ADR-105)
- Consolidated is read-only, labelled, not statutory (ADR-106)
- VAT is company-level; no group VAT return (MULTI_COMPANY.md §8)
- Period close refuses over outstanding intents (MULTI_COMPANY.md §7)
- Stale contributors named, not silently summed (ADR-119)

## Dependencies

TASK-07C-001 must be complete. ADR-105, ADR-106, ADR-119, ADR-016 apply.

## Relevant Files

- `src/VumaRetail.Domain/Registry/GroupReceiptEntities.cs` (extend: `InterCompanyClearingIntent`)
- `src/VumaRetail.Application/Abstractions/Registry/GroupReceiptPorts.cs` (extend: `IConsolidationService`, `ICompanyClearingAccountService`)
- `src/VumaRetail.Application/Registry/ConsolidationQueries.cs` (new)
- `src/VumaRetail.Infrastructure/Registry/ConsolidationService.cs` (new)
- `src/VumaRetail.Infrastructure/Registry/ClearingAccountService.cs` (new)
- `src/VumaRetail.Infrastructure/Finance/NetZeroReconciliationJob.cs` (new)
- `src/VumaRetail.Infrastructure/Finance/PostingRuleSeed.cs` (extend)
- `src/VumaRetail.Infrastructure/Persistence/Configurations/` (extend: clearing intent EF config)
- `src/VumaRetail.Infrastructure/DependencyInjection/` (extend)
- `src/VumaRetail.Web/ConsolidationEndpoints.cs` (new)
- `src/VumaRetail.StoreServer/Program.cs` (extend)
- `tests/VumaRetail.UnitTests/Registry/` (extend)
- `tests/VumaRetail.IntegrationTests/Registry/` (extend)

## Relevant Documentation

`docs/MULTI_COMPANY.md` §§7–8, `docs/DECISIONS.md` ADR-105, ADR-106, ADR-119, ADR-016, `CLAUDE.md` §§7, 8.

## Implementation Requirements

1. `InterCompanyClearingIntent` carries both legs and the group document; immutable once created; settled only when both companies acknowledge (ADR-105)
2. Clearing accounts auto-created per company pair direction from a template in chart of accounts
3. Posting-rule keys: `GroupReceiptAllocated`, `InterCompanyClearingRaised`, `InterCompanySettled` — seeded with default rules
4. Net-zero reconciliation is a scheduled job across databases, run after every intent and on a schedule, with alarm
5. Consolidated reports assembled from each company's published period figures; each carries `AsAt` and names stale contributors (ADR-119)
6. Period close refuses if any inter-company intent is outstanding, and names it
7. No GL account named in this task (§7 rule 12)
8. No module posts to consolidated; it is read-only (ADR-106)

## Data/Database Impact

- New registry table: `inter_company_clearing_intents`
- Extend `finance.posting_rules` with new event types
- Extend `finance.accounts` with clearing-account template
- Reversible migration

## API Impact

New endpoints: `GET /api/v1/reports/consolidated/trial-balance`, `GET /api/v1/reports/consolidated/income-statement`, `GET /api/v1/reports/consolidated/balance-sheet`. Every response carries watermark field.

## Security

Permissions: `group.report.consolidated`. Consolidated is read-only. No write path reaches consolidated data.

## Multi-Company/Tenant Impact

Clearing intent spans two company databases. Each leg runs inside one company's DB. Consolidated reads from N company databases and assembles in registry. Period-close guard checks all companies.

## Sync/Offline Impact

Clearing intents are registry-only. Company-leg clearing entries replicate normally. Consolidated figures are projections, not replicated.

## Acceptance Criteria

1. Clearing balances across group net to zero at every instant
2. Net-zero reconciliation job runs and alarms on mismatch
3. Consolidated income statement eliminates inter-company trade
4. Consolidated output is labelled, watermarked, not statutory
5. Stale contributors named in consolidated response
6. Period close refused with outstanding intent named
7. Clearing accounts auto-created on first use
8. Posting rules seeded for all three event types

## Tests Required

- Domain: InterCompanyClearingIntent lifecycle (create → legs ack → settled)
- Application: ConsolidationService assembly, period-close guard
- Integration: Net-zero assertion after allocations/reversals, clearing account auto-creation
- Property test: 200 randomised allocations/reversals/failures across 3 DBs → clearing nets to zero
- Consolidated income statement elimination test

## Edge Cases

- One company down: its clearing leg stays Pending, net-zero report shows imbalance
- Intent with one leg outstanding past timeout: alarm fires
- Period close attempted with outstanding intent: refused, intent named
- Consolidated with stale contributor: names which company is stale
- Clearing account already exists for pair: no-op

## Definition of Done

- [ ] `dotnet build -c Release` — zero errors
- [ ] Unit tests pass, coverage ≥ 80% on Domain + Application
- [ ] EF migration generated and reversible
- [ ] Posting rules seeded; no GL account named outside rules engine
- [ ] Net-zero reconciliation job exists and is seeded
- [ ] Unapplied-legs report exists

## Follow-up Findings

None yet.

## Work Log

Not started.
