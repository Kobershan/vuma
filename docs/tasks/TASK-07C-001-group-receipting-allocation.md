# Task

## Status

NOT_STARTED

## Stage

Stage 07c — Cross-company money: group receipting, allocation, inter-company clearing, consolidated reporting

## Type

DOMAIN, APPLICATION, INFRASTRUCTURE, DATABASE

## Objective

Implement group receipt capture and allocation: the registry entities (`GroupReceipt`, `GroupReceiptAllocation`, `GroupPaymentRun`, `GroupPaymentAllocation`), the application services (`IGroupReceiptService`), the per-company leg handler, and the API endpoints for capturing and allocating group receipts across companies.

## Why

A customer pays R9 000 once against accounts in three companies. Those companies are separate databases (ADR-099), so nothing here can be one transaction. The money must be captured once in the registry, dispatched to each company as idempotent saga legs, and the unallocated remainder must sit on the registry container in nobody's ledger.

## Scope

- Domain: `GroupReceipt`, `GroupReceiptAllocation`, `GroupPaymentRun`, `GroupPaymentAllocation` entities in `Domain/Registry/GroupReceiptEntities.cs`
- Extend `ArReceipt` with nullable `GroupDocumentId` and `IntentId`
- Extend `ApPayment` with nullable `GroupDocumentId` and `IntentId`
- Application: `IGroupReceiptService` (capture, allocate, reverse), `IGroupReceiptRepository`
- Application: `CaptureGroupReceiptCommand`, `AllocateGroupReceiptCommand`, `ReverseGroupReceiptCommand`
- Application: `GetUnallocatedGroupReceiptsQuery`
- Infrastructure: `GroupReceiptService`, `GroupReceiptRepository`, `GroupReceiptLegHandler`
- Infrastructure: EF configurations for all new entities
- Infrastructure: DI registration
- Infrastructure: Replication registry entries
- Web: `/api/v1/group-receipts` endpoints (capture, allocate, reverse, list unallocated, detail)
- Web: `/api/v1/group-payments` endpoints (capture, allocate)
- Permissions: `group.receipt.capture`, `group.receipt.allocate`, `group.receipt.reverse`, `group.payment.capture`, `group.payment.allocate`
- Tests: domain unit tests, application unit tests

## Out of Scope

Inter-company clearing intents (TASK-07C-002), consolidated reporting (TASK-07C-002), net-zero reconciliation job (TASK-07C-002), period-close guard (TASK-07C-002), posting rule seeding (TASK-07C-002).

## Architecture

Layer: Domain (entities in registry namespace), Application (ports + command handlers), Infrastructure (persistence + saga dispatch + leg handler), Web (Minimal API endpoints). Component: group receipting. Data flow: API → command handler → GroupReceiptService → registry DB (container) + ISagaCoordinator dispatch → per-company leg handler → company DB (AR receipt + posting). No module names a GL account (§7 rule 12). Each leg runs inside the target company's database only (ADR-099, ADR-116).

## Architectural Boundaries

- No command handler opens transactions against two databases (ADR-116)
- Group receipt posts nothing; only legs post, inside one company's DB (ADR-104)
- Each allocation is idempotent, keyed by `(group_receipt_id, allocation_id)` (ADR-116)
- Allocation dispatches through `ISagaCoordinator` (06d)
- Σ allocations ≤ captured amount, enforced in domain aggregate
- CompanyLink must be Active with `SharedReceipting` scope (ADR-122)

## Dependencies

Stages 06c, 06d, 06e, 07 must be complete. ADR-099, ADR-104, ADR-116 apply. No task dependency.

## Relevant Files

- `src/VumaRetail.Domain/Registry/` (new `GroupReceiptEntities.cs`)
- `src/VumaRetail.Domain/Finance/ArReceipt.cs` (extend)
- `src/VumaRetail.Domain/Finance/ApPayment.cs` (extend)
- `src/VumaRetail.Application/Abstractions/Registry/GroupReceiptPorts.cs` (new)
- `src/VumaRetail.Application/Registry/` (new command/query handlers)
- `src/VumaRetail.Infrastructure/Registry/` (new services)
- `src/VumaRetail.Infrastructure/Persistence/Configurations/` (new EF configs)
- `src/VumaRetail.Infrastructure/DependencyInjection/` (extend)
- `src/VumaRetail.Infrastructure/Sync/ReplicationRegistry.cs` (extend)
- `src/VumaRetail.Web/GroupReceiptEndpoints.cs` (new)
- `src/VumaRetail.StoreServer/Program.cs` (extend)
- `tests/VumaRetail.UnitTests/Registry/` (new)
- `tests/VumaRetail.IntegrationTests/Registry/` (new)

## Relevant Documentation

`docs/MULTI_COMPANY.md` §§2, 7, `docs/DECISIONS.md` ADR-104, ADR-116, ADR-122, `CLAUDE.md` §§7, 8.

## Implementation Requirements

1. UUID v7 for all new primary keys (ADR-004)
2. Group receipt is immutable once fully allocated; correction is reversal (§7 rule 7)
3. Each allocation dispatched through `ISagaCoordinator.ExecuteAsync` with idempotency key `(group_receipt_id, allocation_id)`
4. Per-company leg handler resolves target company's `DbContext` via `ICompanyDbContextFactory`, creates `ArReceipt` (with `GroupDocumentId` and `IntentId` set), raises `GroupReceiptAllocated` financial event, posts through that company's posting rules
5. Retry is always safe; recovery from outage is retry, never re-key (ADR-104)
6. No GL account named anywhere in this task (§7 rule 12)
7. CompanyLink check: `SharedReceipting` scope required at allocation time

## Data/Database Impact

- New registry tables: `group_receipts`, `group_receipt_allocations`, `group_payment_runs`, `group_payment_allocations`
- Extend `finance.ar_receipts` with `group_document_id uuid?` and `intent_id uuid?`
- Extend `finance.ap_payments` with `group_document_id uuid?` and `intent_id uuid?`
- Reversible migration

## API Impact

New endpoints: `POST /api/v1/group-receipts`, `POST /api/v1/group-receipts/{id}/allocations`, `POST /api/v1/group-receipts/{id}/reverse`, `GET /api/v1/group-receipts/unallocated`, `GET /api/v1/group-receipts/{id}`, `POST /api/v1/group-payments`, `POST /api/v1/group-payments/{id}/allocations`.

## Security

Permissions: `group.receipt.capture`, `group.receipt.allocate`, `group.receipt.reverse`, `group.payment.capture`, `group.payment.allocate`. CompanyLink `SharedReceipting` scope checked at allocation. Connection details encrypted at rest in registry.

## Multi-Company/Tenant Impact

Group receipt lives in registry DB. Each allocation targets one company's DB. No cross-database transaction. Tenant-scoped on all entities.

## Sync/Offline Impact

Group receipt entities are registry-only, not replicated to company DBs. Company-leg receipts replicate normally (StoreToCloud, AppendOnly). Saga legs dispatched through registry outbox.

## Acceptance Criteria

1. Group receipt captured with R9 000, allocated R1 000 / R3 000 / R5 000 across 3 companies
2. Three AR receipts exist in three company databases
3. Σ allocations ≤ captured amount enforced
4. Each allocation is idempotent; retried call posts exactly once
5. Unallocated remainder (R0 in the full case) sits on group receipt
6. Partial allocation: R9 000 captured, R4 000 allocated, R5 000 unallocated — nothing of the R5 000 appears in any company's ledger
7. Reversal dispatches reversing legs to every company involved

## Tests Required

- Domain: GroupReceipt allocation sum check, status transitions, immutability after full allocation
- Application: CaptureGroupReceiptCommandHandler, AllocateGroupReceiptCommandHandler, ReverseGroupReceiptCommandHandler
- Integration: 3-database example (R9 000 across 3 companies), partial allocation, retry idempotency
- Architecture: no GL account named in this task's code

## Edge Cases

- Allocate to same invoice from two allocations: must not over-allocate
- Company database down mid-allocation: leg stays Pending, other legs apply
- Retry after company comes back: leg applies exactly once
- Allocate with zero amount: refused
- Reverse already-reversed receipt: refused
- CompanyLink not Active or missing SharedReceipting scope: refused

## Definition of Done

- [ ] `dotnet build -c Release` — zero errors
- [ ] Unit tests pass, coverage ≥ 80% on Domain + Application
- [ ] EF migration generated and reversible
- [ ] Endpoints appear in OpenAPI
- [ ] No GL account named outside the rules engine

## Follow-up Findings

None yet.

## Work Log

Not started.
