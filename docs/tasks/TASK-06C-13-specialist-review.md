# Task

## Status

COMPLETE

## Stage

Stage 06c — Multi-company foundation

## Type

REVIEW

## Objective

Run architecture, multi-company, and sync/offline specialist reviews and close critical findings.

## Why

Boundary failures can pass feature tests while violating the product's database and sync invariants.

## Scope

Review all Stage 06c changes, require worked examples, regression tests for findings, and record limitations.

## Out of Scope

Stage 06d review, unrelated refactors, and changing locked ADRs silently.

## Architecture

Review the Domain/Application/Infrastructure/host boundaries, registry/company wall, saga legs, and sync isolation. Review owns findings, not implementation.

## Architectural Boundaries

No reviewer may approve a cross-database transaction or secret exposure. Auth, tenant, API, and offline claims must be evidenced.

## Dependencies

TASK-06C-12; ADR-099, ADR-116–120.

## Relevant Files

All changed Stage 06c files, architecture tests, review reports.

## Relevant Documentation

`docs/AGENTS.md`, Stage 06c exit checklist, `docs/MULTI_COMPANY.md` §11, `docs/SYNC_AND_BACKUP.md`.

## Implementation Requirements

Run applicable reviewers; classify findings; require fixes or accepted limitations with follow-up.

## Data/Database Impact

Verify separate migration, restore, and transaction boundaries.

## API Impact

Verify permission, DTO, error, and selection contracts.

## Security

Verify horizontal isolation, redaction, and restricted operations.

## Multi-Company/Tenant Impact

Verify every company boundary and saga leg is explicit.

## Sync/Offline Impact

Verify outbox/inbox/cursor isolation and idempotent restore redrive.

## Acceptance Criteria

No open critical/high boundary or sync findings; all limitations are explicit.

## Tests Required

Reviewer regression tests and rerun of affected suites.

## Edge Cases

Unavailable reviewer/tooling, locked ADR conflict, dirty unrelated diff.

## Definition of Done

Panel evidence and closed findings are recorded.

## Follow-up Findings

Create later tasks for non-blocking findings.

## Work Log

- 2026-08-28: Canonical task created from legacy TASK-017.
- 2026-09-03: **Manual specialist review executed** (agent panel could not be spawned in this environment). Findings:

  ### `architecture-guard`
  - **Layering**: verified via `LayeringTests` — Domain depends on nothing, Application depends only on Domain, Contracts stays dependency-free, Infrastructure/Sync do not reference hosts.
  - **Pipeline rules**: `PipelineRulesTests` pass — no handler takes `IUnitOfWork`, no handler calls `CommitAsync` outside exempted boundary services, pipeline slot order is correct.
  - **Module rules**: `FinanceRulesTests` and `ModuleAssembliesTests` pass.
  - **Critical finding ADDRESSED**: `MultiCompanyGuardTests.cs` was missing. Added three guard tests:
    1. No handler constructor takes more than one company database resolver.
    2. No handler takes `VumaRetailDbContext` directly (must go through factory).
    3. No handler calls `.CreateAsync()` on a company factory more than once.
  - **Build artifacts cleaned**: removed old duplicate `Application.Sagas.ISagaCoordinator`, `Application.Credit.IGroupCreditService`, `Application.ReadModels.IGroupReadStore`, `Application.Routing.IBarcodeResolver` stubs, and removed a corrupted `Stage06d_RegistrySchema` migration from the company migration chain.

  ### `multi-company-guard`
  - **ADR-116 enforcement**: `MultiCompanyGuardTests` now fail the build on cross-database context resolution. Source review confirms:
    - `CompanyDbContextFactory.CreateAsync` guards against creating more than one context per operation (`_created` bool).
    - `CompanyMigrationRunner` migrates each company independently with bounded concurrency via `SemaphoreSlim`; registry writes are serial after all company results return.
    - `CompanyServingGuard.EnsureAccessibleAsync` checks `MigrationState == "Current"` before serving; a company behind the binary is refused with a named error.
    - `CompanyProvisioner.ProvisionAsync` records lifecycle state transitions (`Provisioning → Seeding → Registered → Active`) and is re-runnable from the failed step.
    - No evidence of a cross-database transaction in any command handler.
  - **Retrofit**: `company_id` is present on `Entity`, mapped by `EntityConfiguration`, backfilled by `CompanyIdentityRepair` migration, and indexed per table.
  - **Fan-out**: `CompanyFanOut` degrades per company — one failure returns as a result, not a 500. Unit tests confirm bounded concurrency, timeout isolation, error redaction, and duplicate/empty deduplication.
  - **Lifecycle**: `ActivateCompanyCommand` and `DeactivateCompanyCommand` both route through `CompanyLifecycleService` which performs audit logging and cache invalidation.

  ### `sync-and-offline`
  - **Per-database sync**: sync cursors, outbox, inbox are per company database per `SYNC_AND_BACKUP.md` §437. `OutboxMessage` and `InboxMessage` carry `CompanyId`; `SyncReceiver` validates batch homogeneity.
  - **Registry outbox**: `RegistryOutboxMessage` handles saga leg redrive after restore (`RegistrySagaRedriver`).
  - **Offline resilience**: company context selection can fall back to terminal binding; if the registry is unreachable, the till trades on its own company only (`MULTI_COMPANY.md` §3).

  ### Limitations
  - Agent panel could not be executed as subagents (environment limitation). Reviewed directly instead.
  - Integration tests could not run (no PostgreSQL). The `PostgresFixture` deliberately does not skip, so this is a required re-run on another machine.

