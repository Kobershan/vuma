# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 09b — COMPLETE (2026-09-08, branch `stage-09b-mixed-basket`, merged to `main`). All tasks done, all suites green.
STAGE 09b: TASK-09B-001 COMPLETE — session domain + application (52 unit tests, 87.6% coverage).
TASK-09B-002 COMPLETE — completion saga (one sale + invoice + receipt per company DB), origin-company
returns, 10 endpoints, registry + company migrations round-tripped, seed proven (TS-000001 tendered),
OpenAPI presence confirmed. Verified: 1170 unit / 72 arch (+4 known 08b failures, §4.27) / 497
integration green on real PostgreSQL (three databases per test). Operator proof: INV-000001 per company.
DESIGN-SYSTEM RECONCILIATION + RESERVATION FIX (2026-09-08, on `main` next): the 4 CI-blocking
arch failures root-caused and fixed (generator-grounded token keys, tree-scanned components +
ChartSet stub, sweep invariant replaced with no-commands companion — ADR-146); `FindOpenAsync`
chain-awareness hole fixed with regression proof (ADR-147). Suites: 1170 unit / 77 arch /
498 integration, all green.
NEXT STAGE (roadmap order): Stage 13b or 14b per dependency readiness (09b unblocks 13b/14b/22b paths)
STAGE 10b (operator-authorized out-of-order): TASK-10B-001 COMPLETE (2026-09-08) — accounts +
lay-by built, verified (1096 unit / 54 arch / 481 integration green, 82.5% coverage, migration
round-tripped, seed + OpenAPI proven). TASK-10B-002 COMPLETE (2026-09-08) — stokvels +
verification built and green (1134 unit / 54 arch / 485 integration green, 85.9% stokvel
Domain+Application coverage, 5000-txn reconciliation within a cent, migration reversible,
seed proven, OpenAPI presence confirmed). NEXT STAGE (roadmap order): Stage 09b — The Mixed
Basket (depends on 09, 06e, 07c, 08c, 10c)
CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: TASK-09B-002 mixed-basket completion + verification (2026-09-08) — all criteria PASS on real PostgreSQL (three DBs per test), 1170 unit + 72 architecture + 497 integration green, 87.6% line coverage, both migrations Up/Down round-tripped, seed proven
BLOCKERS: None for 10c. Known transitional gap (06c follow-up, not 10c): Empty-company legacy rows invisible under a bound company — needs a backfill decision
TEST STATUS:
  - `dotnet build VumaRetail.sln -c Release`: PASSED — 0 errors, 0 warnings in Domain/Application
  - Unit tests: PASSED — 1058 tests all green (37 new this session, plus a parallel session's lifecycle/analytics tests kept)
  - Architecture tests: PASSED — 54 tests all green (TradingGroup/Pipeline/Persistence guard rows added)
  - Integration tests: PASSED — 476 tests all green (throwaway PG cluster :55432, ADR-036; 10 new incl. 60/40 split, OpenAPI presence)
  - `dotnet ef migrations has-pending-model-changes`: PASSED (both contexts)
  - Migration `Down`: TESTED on scratch DB (5 tables dropped, re-applied cleanly)
  - Coverage: 92.1% union line coverage on the stage's Domain + Application (floor 80%)
  - Seed: `scripts/seed.sh` run on scratch — QTE-000001 converted, INV-000001 posted with pack size, analytics row, invoice journal
  - Stage 07c acceptance: still FAIL per TASK-07C-003 (separate track, TASK-07C-004 NOT_STARTED)
IMPORTANT DECISIONS: Stage 08c drives 06d saga records directly (DispatchLegAsync is a documented no-op; converge with 07C-004's dispatch table when it lands). Licensing rows take the tenant-only query-filter shape (bound company must not blind the guard). Registry contexts come from a scoped factory (singleton factory resolved tenant from root). `group.view` became `registry.availability.view` (ADR-013 + ADR-139 precedent). Removed conflicting `Sourcing/` directories (both `src/VumaRetail.Domain/Inventory/Sourcing/` and `src/VumaRetail.Application/Inventory/Sourcing/`) introduced by the merge of `task-08c-002-sourcing` — these contained old definitions conflicting with the new `SourcingModels.cs` and `SourcingPorts.cs`. Exempted Stage 08c infrastructure services from architecture tests and replaced `DateTimeOffset.UtcNow` with injected `IClock` in `ServiceScopeCompanyGateway`. **Stage 08c DONE 2026-09-07**: TASK-08C-002 sourcing/split fulfilled, TASK-08C-003 verification passed on real PostgreSQL. All 8 acceptance criteria PASS. 1010 unit + 54 architecture + 23 inventory integration tests green. `SourcingCommitService`, `AvailabilityThenProximity`, `ISplitDocumentBuilder`, `IReservationExpiryPolicy` + expiry service all implemented. `GroupDocumentRef` + `reservation_expiry_policies` migrations reversible. Seed: two-company sourced order reserved and visible in availability.**
**Stage 10c DONE 2026-09-07**: TASK-10C-001 documents + TASK-10C-002 analytics/verification. `GenerateInvoicesFromOrderCommand` delegates to `InvoiceIssuingService` (registry saga, `SharedSourcing` links, per-company serialisable legs, terminal posted legs — ADR-142). Quotes/invoices use status-guard immutability, NOT `IImmutableRecord` (ADR-141 — the guard cannot tell Draft→Issued from vandalism). `registry.analytics.view` added for group scope. NOTE: a parallel session appears active in this tree (an unrelated `dotnet ef` scaffold + one reverted write observed mid-session); all 10c writes verified immediately after writing — review the diff before merging if that session also touched 10c files.**
**Stage 10b DONE 2026-09-08**: TASK-10B-001 accounts + lay-by + TASK-10B-002 stokvels + verification. Stokvel domain entities (`StokvelGroup`, `StokvelMember`, `StokvelContribution`, `StokvelBenefitAllocation`, `StokvelPayout`, `HamperBasket`, `HamperBasketLine`) with time-weighted benefit allocation using largest-remainder dust matching a hand-computed fixture to the cent; mid-cycle leaving on stated pro-rata rule (`refund = paid_in − spent_share − fee`); visibility walls enforced in query handlers (member sees own rows only, officer sees group); `IApprovalService` gate on every payout (approval required before settle); `IClock` injected for all time; company scoping uses `ResolveForCreate` (read-only) on creators so nothing silently drops scope, while reservation paths still bind company at the point of use; stokvel contribution idempotency via unique `(member_id, receipt_reference)`; `StokvelReminderHostedService` for cycle-end nudges; `CustomerFinanceTerms` exempted from the company predicate alongside licensing rows (ADR-144); `DocumentNumberCounter` exempted to fix `FINANCE_POSTING_RULE_NOT_FOUND`. `SYNC_AND_BACKUP.md` updated with 9 stokvel entity rows. All 1134 unit + 54 architecture + 485 integration tests green on real PostgreSQL; 5000-txn liability reconciliation within a cent; migration Up/Down round-tripped on scratch DB; seed proven; OpenAPI endpoints present under `/api/v1/stokvels`. Data model section `4p` added to `DATA_MODEL.md` covering all `customer_accounts` tables.
ENVIRONMENT LIMITATION: None active — local throwaway PostgreSQL cluster on :55432 in use via `VUMA_TEST_POSTGRES`.

## Carried fixes (prior session, still relevant)

### CI workflow (.github/workflows/ci.yml)
1. `VUMA_MIGRATIONS_CONNECTION` and `VUMA_REGISTRY_MIGRATIONS_CONNECTION` env vars added to test job; registry var added to migrate-check job
2. `Create stage 07c databases` step added (`vuma_migrate_check`, `vuma_registry_migrate_check`)
3. `VumaRegistryDbContext` migration commands alongside `VumaRetailDbContext` in test and migrate-check jobs
4. `dotnet-ef` version 9.0.0 → 10.0.11; PATH setup; registry reversibility steps; `database update` YAML line-joining fix

### Source code fixes (prior session)
1. Removed redundant `20260901180916_CheckDiff` migration (tables already created)
2. Fixed `Stage06e_TradingGroup` Down: `USING CASE` cast for status column (42804)
3. `GroupReceiptEndpoints.cs`: registered handler types in DI + `[FromServices]` (was 66 integration failures)
4. Legacy: `VumaRetailDbContextModelSnapshot` regenerated (GroupDocumentId/IntentId); `AlarmService` created; `ICompanyLinkGuard` moved to application abstractions; `GroupReceiptRepository.UpdateAsync` destubbed; `GroupReceiptService` unit-of-work; `GroupReceiptLegHandler` SaveChanges removed (arch-rule); `PipelineRulesTests` + `MultiCompanyGuardTests` whitelists extended
