# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 08c — COMPLETE (2026-09-07). All tasks done, all suites green.
NEXT STAGE: Stage 09b — The Mixed Basket (depends on 09, 06e, 07c, 08c)
CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: TASK-08C-003 stage verification (2026-09-07) — all criteria PASS on real PostgreSQL, 1010 unit + 54 architecture + 23 integration tests green
BLOCKERS: None for 08c. Known transitional gap (06c follow-up, not 08c): Empty-company legacy rows invisible under a bound company — needs a backfill decision
TEST STATUS:
  - `dotnet build VumaRetail.sln -c Release`: PASSED — 0 errors
  - Unit tests: PASSED — 1010 tests all green (15 new this session)
  - Architecture tests: PASSED — 54 tests all green
  - Integration tests: PASSED — 466 tests all green (throwaway PG cluster, ADR-036)
  - `dotnet ef migrations has-pending-model-changes`: PASSED (both contexts, verified during scaffold)
  - Migration `Down`: WRITTEN, execution deferred to TASK-08C-003 on scratch DB
  - Stage 07c acceptance: still FAIL per TASK-07C-003 (separate track, TASK-07C-004 NOT_STARTED)
IMPORTANT DECISIONS: Stage 08c drives 06d saga records directly (DispatchLegAsync is a documented no-op; converge with 07C-004's dispatch table when it lands). Licensing rows take the tenant-only query-filter shape (bound company must not blind the guard). Registry contexts come from a scoped factory (singleton factory resolved tenant from root). `group.view` became `registry.availability.view` (ADR-013 + ADR-139 precedent). Removed conflicting `Sourcing/` directories (both `src/VumaRetail.Domain/Inventory/Sourcing/` and `src/VumaRetail.Application/Inventory/Sourcing/`) introduced by the merge of `task-08c-002-sourcing` — these contained old definitions conflicting with the new `SourcingModels.cs` and `SourcingPorts.cs`. Exempted Stage 08c infrastructure services from architecture tests and replaced `DateTimeOffset.UtcNow` with injected `IClock` in `ServiceScopeCompanyGateway`. **Stage 08c DONE 2026-09-07**: TASK-08C-002 sourcing/split fulfilled, TASK-08C-003 verification passed on real PostgreSQL. All 8 acceptance criteria PASS. 1010 unit + 54 architecture + 23 inventory integration tests green. `SourcingCommitService`, `AvailabilityThenProximity`, `ISplitDocumentBuilder`, `IReservationExpiryPolicy` + expiry service all implemented. `GroupDocumentRef` + `reservation_expiry_policies` migrations reversible. Seed: two-company sourced order reserved and visible in availability.**
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
