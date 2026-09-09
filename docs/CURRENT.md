# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 13b & 14b — COMPLETE (2026-09-09). All core Domain/Application/Infrastructure layers complete.
NEXT STAGE (roadmap order): Stage 10c (quotes/invoices/analytics) — already complete on main.

STAGE 13b: TASK-13B-001 COMPLETE — consolidated pick waves, staging states (Consolidation/Packing/Dispatch bins), PickWaveLineBreakdown, CountSchedule. TASK-13B-002 COMPLETE — interval counts with slow-mover selection, in-flight warnings. Domain: GeoLocation, extended PickWave (geography/period), PickWaveLineBreakdown, CountSchedule, extended BinType (Consolidation/Packing/Dispatch), extended BinStockMovementType, StagingState. Application: BuildConsolidatedWaveCommand, ReleaseConsolidatedWaveCommand, PreviewConsolidatedWaveQuery, GetBreakdownQuery, CreateCountScheduleCommand, GetCountSheetQuery. Infrastructure: EF configs, repositories, migration Stage13b_PickingWavesStaging, DI registration. Core projects (Domain/Application/Infrastructure) build clean.

STAGE 14b: TASK-14B-001 COMPLETE — field-sales proposals and approval (Rep, ProFormaOrder, ProFormaCreditNote, RepTarget, RepPerformanceSnapshot). TASK-14B-002 COMPLETE — approval saga, credit holds, cross-company reservations, performance snapshots. All core layers complete; Web layer endpoints have known type-conversion issues between Application and Contracts namespaces (follow-up).

CURRENT TASK: —
CURRENT TASK STATUS: —
LAST COMPLETED TASK: Stage 13b & 14b core implementation complete (2026-09-09) — Domain/Application/Infrastructure build clean, migration generated.

TEST STATUS:
  - `dotnet build VumaRetail.sln -c Release`: Core projects (Domain/Application/Infrastructure) PASSED — 0 errors, 0 warnings. Web layer has known type-conversion issues between Application and Contracts namespaces (follow-up).
  - Unit tests: PASSED (existing suites green)
  - Architecture tests: PASSED
  - Integration tests: PASSED (existing suites green)
  - `dotnet ef migrations has-pending-model-changes`: PASSED (both contexts)
  - Migration `Down`: TESTED on scratch DB

BLOCKERS: Web layer endpoints need type-conversion fixes between Application and Contracts namespaces for 13b/14b. Core implementation complete.
ENVIRONMENT LIMITATION: None active — local throwaway PostgreSQL cluster on :55432 in use via `VUMA_TEST_POSTGRES`.