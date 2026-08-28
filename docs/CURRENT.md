# CURRENT STATE — Vuma Retail

> This is the small session handoff. Keep it current and concise. Historical detail belongs in
> `PROGRESS.md`; architecture rationale belongs in `DECISIONS.md`.

CURRENT STAGE: Stage 06c — Multi-company foundation
CURRENT TASK: TASK-06C-03 — Company routing
CURRENT TASK STATUS: NEEDS_VERIFICATION
NEXT READY TASK: TASK-06C-04
LAST COMPLETED TASK: TASK-06C-01 — Registry foundation
BLOCKERS: Registry routing integration checks remain unverified. Docker is unavailable; after `scripts/pg-test.sh start`, the local PostgreSQL run fails during shared fixture setup because `VumaRetailDbContext` has pending model changes in the existing company migration chain.
TEST STATUS: `dotnet build src/VumaRetail.Infrastructure/VumaRetail.Infrastructure.csproj --no-restore --nologo` — succeeded with existing repository warnings; `dotnet test tests/VumaRetail.UnitTests/VumaRetail.UnitTests.csproj --no-restore --nologo` — 829 passed; `dotnet test tests/VumaRetail.ArchitectureTests/VumaRetail.ArchitectureTests.csproj --no-restore --nologo` — 35 passed; `dotnet test tests/VumaRetail.IntegrationTests/VumaRetail.IntegrationTests.csproj --filter 'FullyQualifiedName~Registry' --no-restore --nologo` — 2 failed before execution because Docker was unavailable; with `VUMA_TEST_POSTGRES='Host=127.0.0.1;Port=55432;Database=postgres;Username=vuma;Password=vuma'`, 2 failed during shared fixture setup on `VumaRetailDbContext` pending model changes.
IMPORTANT DECISIONS: ADR-099, ADR-116, ADR-117, ADR-118, ADR-119, and ADR-120 govern Stage 06c.
