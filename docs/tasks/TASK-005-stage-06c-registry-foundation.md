# TASK-005 — Registry database model and migration chain

## Objective
Define and implement the isolated registry company model, context, DI/design-time wiring, and reversible migration.

## Why
Every later Stage 06c capability needs a registry-first physical boundary and a trustworthy lifecycle record.

## Scope
`registry.companies`, lifecycle state, encrypted secret reference, schema/migration state, registry EF context and migration history.

## Out of scope
Routing, groups, sagas, provisioning orchestration, API, retrofit, backup, sync, and deactivation commands.

## Affected files/components
`src/VumaRetail.Domain/Registry/Company.cs`; `src/VumaRetail.Infrastructure/Persistence/VumaRegistryDbContext.cs`;
`src/VumaRetail.Infrastructure/Persistence/RegistryDesignTimeDbContextFactory.cs`;
`src/VumaRetail.Infrastructure/RegistryMigrations/`; `tests/VumaRetail.UnitTests/Registry/CompanyTests.cs`;
`tests/VumaRetail.IntegrationTests/Persistence/MigrationTests.cs`.

## Dependencies
Stages 01, 04, 06 and 07; no Stage 06c task dependency.

## Relevant references
Stage 06c; `docs/MULTI_COMPANY.md` §§1–2, 9; `docs/DATA_MODEL.md` §4l; ADR-099, ADR-117, ADR-118; `docs/EXECUTION_STANDARD.md`.

## Implementation requirements
Use a separate context and connection; map the `registry` schema; keep no plaintext connection string; enforce tenant-scoped code and document-prefix uniqueness; default new companies to `Provisioning` and inactive; make `Down` reversible.

## Acceptance criteria
The registry migration creates only the agreed company shape and its own history table; active serving requires both active state and flag; migration applies and rolls back against a real registry database.

## Tests
Domain lifecycle tests; model/constraint tests; migration up/down integration test; no-secret-column assertion.

## Edge cases
Duplicate code/prefix, invalid lifecycle transition, null optional identity fields, failed migration, inactive `Active` row.

## Security considerations
Store only an encrypted secret reference; never log/export credentials; require tenant scoping and least-privileged registry access.

## Architectural decision impact
Implements ADR-099 and the registry portions of ADR-117/118; no new decision unless a conflict is found and recorded.

## Definition of done
Code, migration, tests, review, task evidence, and green checkpoint are complete; unrelated findings are follow-up tasks.

## Status
COMPLETE

## Work log
- 2026-08-27: Planning pass decomposed the registry foundation. Prior working-copy history claimed source implementation; this plan does not treat that claim as a green completion.
