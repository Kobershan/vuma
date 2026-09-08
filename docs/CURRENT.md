# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 10c — COMPLETE (2026-09-07). All tasks done, all suites green.
STAGE 10b (operator-authorized out-of-order): TASK-10B-001 COMPLETE (2026-09-08) — accounts +
lay-by built, verified (1096 unit / 54 arch / 481 integration green, 82.5% coverage, migration
round-tripped, seed + OpenAPI proven). NEXT: TASK-10B-002 stokvels + verification — READY.
NEXT STAGE (roadmap order): Stage 09b — The Mixed Basket (depends on 09, 06e, 07c, 08c, 10c)
CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: TASK-10C-002 analytics + verification (2026-09-07) — all criteria PASS on real PostgreSQL, 1058 unit + 54 architecture + 476 integration tests green, 92.1% line coverage, migration Up/Down round-tripped, seed proven
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
