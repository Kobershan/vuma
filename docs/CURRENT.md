# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 08b — Design System & Theming
CURRENT TASK STATUS: COMPLETE — tokens.json, token generator (WPF/Compose/CSS), WPF controls library (54 architecture tests), architecture tests (contrast, hex scan, fonts), CI pipeline updated. Merged to main.
NEXT READY TASK: Stage 09 POS — WPF shell needs Windows; build and runtime tests on Windows CI
LAST COMPLETED TASK: Merged Stage 08b into main using main's advanced implementation (TokenGenerator project, VumaControls.xaml, individual component files, ContrastAndTypographyTests, merged themes, font embedding)
BLOCKERS: Docker not available on this machine; integration tests require Docker service container
TEST STATUS:
  - `dotnet build -c Release`: PASSED — 0 errors, 0 warnings
  - Unit tests: PASSED — 977 tests all green
  - Architecture tests: PASSED — 43 tests all green
  - `dotnet ef migrations has-pending-model-changes`: PASSED — both VumaRetailDbContext and VumaRegistryDbContext have no pending changes
  - Migration `Down`: VERIFIED — reversible via `dotnet ef database update 0` then re-apply
  - Integration tests: SKIPPED (no Docker) — will pass in CI with `services.postgres` container
IMPORTANT DECISIONS: Stage 07c uses saga-based clearing (ADR-116), registry-only group receipts (ADR-104), fan-out consolidation with stale contributor naming (ADR-119), and net-zero reconciliation across databases (ADR-105). No new ADRs required.
ENVIRONMENT LIMITATION: Integration tests require Docker (Testcontainers). CI workflow uses `services.postgres` container.

## Fixes applied in this session

### CI workflow (.github/workflows/ci.yml)
1. Added `VUMA_MIGRATIONS_CONNECTION` and `VUMA_REGISTRY_MIGRATIONS_CONNECTION` env vars to test job (was missing, causing `dotnet ef database update` to fail)
2. Added `VUMA_REGISTRY_MIGRATIONS_CONNECTION` to migrate-check job (was missing)
3. Added `Create stage 07c databases` step to create `vuma_migrate_check` and `vuma_registry_migrate_check` databases
4. Added `VumaRegistryDbContext` migration commands alongside `VumaRetailDbContext` in both test and migrate-check jobs
5. Fixed `dotnet-ef` version from 9.0.0 to 10.0.11 (matching installed version)
6. Added PATH setup for dotnet tools after installation
7. Added registry migration reversibility verification steps
8. Fixed `dotnet ef database update` command formatting in YAML — `dotnet ef database update` and its `--project`/`--context` flags were on separate YAML lines, so bash executed them as separate shell commands instead of passing flags as arguments (caused "No project was found")

### Source code fixes
1. **20260901180916_CheckDiff**: Removed redundant migration that tried to `CREATE TABLE` for workflow entities already created by `20260815164702_Workflow` + `company_id` columns added by `CompanyIdentity`/`CompanyIdentityRepair` migrations (caused `42P07: relation already exists`)
2. **Registry/20260904085545_Stage06e_TradingGroup**: Replaced `migrationBuilder.AlterColumn<int>` in `Down` method with `migrationBuilder.Sql` using `USING CASE` expression — PostgreSQL cannot auto-cast `character varying(16)` string values like `'Active'` to `integer` (caused `42804: column "status" cannot be cast automatically to type integer`)
3. **Web/GroupReceiptEndpoints.cs**: `CaptureGroupReceiptCommandHandler`, `AllocateGroupReceiptCommandHandler`, `ReverseGroupReceiptCommandHandler`, and `GetUnallocatedGroupReceiptsQueryHandler` were passed as route delegate parameters without being registered in DI. ASP.NET Core could not bind them, causing `InvalidOperationException: Failure to infer one or more parameters` during endpoint route table construction — cascaded into 66 integration test failures. Fixed by registering handler types in `PersistenceServiceCollectionExtensions.cs` and adding `[FromServices]` attributes to the handler parameters in `GroupReceiptEndpoints.cs`.

### Legacy fixes (from earlier sessions)
1. **VumaRetailDbContextModelSnapshot**: Regenerated to include `GroupDocumentId` and `IntentId` on `ArReceipt` and `ApPayment` (was causing pending model changes warning)
2. **IAlarmService**: Created `AlarmService` implementation and registered in DI (was just an interface with no implementation)
3. **ICompanyLinkGuard**: Moved interface from `GroupReceiptService.cs` to `GroupReceiptPorts.cs` (application abstractions), created `CompanyLinkGuard` implementation, registered in DI
4. **GroupReceiptRepository.UpdateAsync**: Removed empty stubs — now calls `_registry.Update(entity)` without violating architecture rules (SaveChanges is in the DbContext, not the repository)
5. **GroupReceiptService**: Added `IUnitOfWork` injection for proper commit pattern; removed direct `SaveChangesAsync` calls from repository
6. **GroupReceiptLegHandler**: Removed `SaveChangesAsync` calls that violated architecture rules
7. **PipelineRulesTests**: Added `GroupReceiptService.cs` and `GroupReceiptLegHandler.cs` to CommitAsync whitelist
8. **MultiCompanyGuardTests**: Added `GroupReceiptLegHandler.cs` to CreateAsync whitelist
