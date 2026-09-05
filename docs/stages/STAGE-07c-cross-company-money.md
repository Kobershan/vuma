# STAGE 07c — Cross-company money: group receipting, allocation, inter-company clearing, consolidated reporting

**Status:** CODE_COMPLETE · **Depends on:** 07, 06c, 06d · **Reference reading:** `docs/MULTI_COMPANY.md` §2, §7, §8, `docs/DECISIONS.md` ADR-104, ADR-105, ADR-106, ADR-116, ADR-016, `CLAUDE.md` §7 rules 7, 12

## Task index

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-07C-001 | Implement group receipting and allocation | Stages 06c, 06d, 06e, 07 | CODE_COMPLETE |
| TASK-07C-002 | Implement inter-company clearing and consolidated reporting | TASK-07C-001 | CODE_COMPLETE |
| TASK-07C-003 | Complete Stage 07c verification | TASK-07C-001, TASK-07C-002 | IN_PROGRESS |

## 07c-MAP-01 — Architecture decomposition and implementation task map

### WHAT / WHY

**WHAT:** Build the registry container that captures money arriving once for several companies, the saga legs that turn allocations into per-company receipts, the paired clearing intents that keep every company's books balanced on their own, and the consolidated read model that shows the group without ever posting to it.

**WHY:** A customer pays R9 000 once against accounts in three companies. Those companies are separate databases (ADR-099), so nothing here can be one transaction. The money must be captured once in the registry, dispatched to each company as idempotent saga legs, and reconciled so clearing balances net to zero across all databases at every instant.

### Affected layers/components

| Layer | Component | Change |
|---|---|---|
| **Domain** (`VumaRetail.Domain`) | `Registry/GroupReceiptEntities.cs` | NEW: `GroupReceipt`, `GroupReceiptAllocation`, `GroupPaymentRun`, `GroupPaymentAllocation`, `InterCompanyClearingIntent` |
| **Domain** | `Finance/ArReceipt.cs` | EXTEND: add nullable `GroupDocumentId` and `IntentId` |
| **Domain** | `Finance/ApPayment.cs` | EXTEND: add nullable `GroupDocumentId` and `IntentId` |
| **Application** (`VumaRetail.Application`) | `Abstractions/Registry/GroupReceiptPorts.cs` | NEW: `IGroupReceiptService`, `IGroupPaymentService`, `IConsolidationService`, `IGroupReceiptRepository` |
| **Application** | `Registry/GroupReceiptCommands.cs` | NEW: `CaptureGroupReceiptCommand`, `AllocateGroupReceiptCommand`, `ReverseGroupReceiptCommand` |
| **Application** | `Registry/ConsolidationQueries.cs` | NEW: `GetConsolidatedTrialBalanceQuery`, `GetConsolidatedIncomeStatementQuery` |
| **Infrastructure** (`VumaRetail.Infrastructure`) | `Registry/GroupReceiptService.cs` | NEW: implementation of `IGroupReceiptService` |
| **Infrastructure** | `Registry/GroupReceiptRepository.cs` | NEW: registry-side persistence for group entities |
| **Infrastructure** | `Registry/ConsolidationService.cs` | NEW: fan-out read + assembly of consolidated reports |
| **Infrastructure** | `Registry/GroupReceiptLegHandler.cs` | NEW: per-company leg handler (runs inside target company DB) |
| **Infrastructure** | `Persistence/Configurations/` | NEW: EF configurations for all new entities; extend `ArReceipt` and `ApPayment` |
| **Infrastructure** | `DependencyInjection/` | EXTEND: register new services |
| **Infrastructure** | `Sync/ReplicationRegistry.cs` | EXTEND: register replicated entities |
| **Finance** (`VumaRetail.Finance`) | `PostingRuleSeed.cs` | EXTEND: seed `GroupReceiptAllocated`, `InterCompanyClearingRaised`, `InterCompanySettled` rules |
| **Finance** | `ClearingAccountService.cs` | NEW: auto-create inter-company clearing accounts per company pair |
| **Finance** | `NetZeroReconciliationJob.cs` | NEW: scheduled job across databases with alarm |
| **Web** (`VumaRetail.Web`) | `GroupReceiptEndpoints.cs` | NEW: `/api/v1/group-receipts` endpoints |
| **Web** | `ConsolidationEndpoints.cs` | NEW: `/api/v1/reports/consolidated/*` endpoints |
| **StoreServer** | `Program.cs` | EXTEND: map new endpoint modules |
| **Tests** | `UnitTests/Registry/GroupReceipt*` | NEW: domain + application tests |
| **Tests** | `IntegrationTests/Registry/GroupReceipt*` | NEW: multi-DB integration tests |
| **Tests** | `ArchitectureTests/MultiCompanyGuardTests.cs` | EXTEND: assert clearing nets to zero |

### Data rules

| Rule | Source | Enforcement |
|---|---|---|
| Group receipt lives in registry DB, posts nothing | ADR-104 | Domain: no `IFinancialEvent` from `GroupReceipt` |
| Σ allocations ≤ captured amount | CLAUDE.md §7 rule 7 | Domain: `GroupReceipt.Allocate()` check constraint |
| Each allocation is idempotent, keyed by `(group_receipt_id, allocation_id)` | ADR-116 | Application: `ISagaCoordinator.ExecuteAsync` with idempotency key |
| Inter-company clearing nets to zero | ADR-105 | Infrastructure: `NetZeroReconciliationJob` scheduled cross-DB |
| Period close refuses over outstanding intent | MULTI_COMPANY.md §7 | Application: `IPeriodCloseGuard` check before close |
| No module names a GL account | CLAUDE.md §7 rule 12 | Application: `IFinancialEvent` has no account reference |
| Clearing lines carry intent id | ADR-105 | Domain: `InterCompanyClearingIntent.Legs[].IntentId` |
| Consolidated is labelled, read-only, not statutory | ADR-106 | Application: watermark on every consolidated response |
| VAT is company-level, no group VAT return | MULTI_COMPANY.md §8 | Application: consolidated report excludes VAT aggregation |

### API rules

| Endpoint | Method | Permission | Notes |
|---|---|---|---|
| `/api/v1/group-receipts` | POST | `group.receipt.capture` | Capture; returns group receipt id |
| `/api/v1/group-receipts/{id}/allocations` | POST | `group.receipt.allocate` | Allocate; dispatches saga legs |
| `/api/v1/group-receipts/{id}/reverse` | POST | `group.receipt.reverse` | Reverses intent; dispatches reversing legs |
| `/api/v1/group-receipts/unallocated` | GET | `group.receipt.capture` | Unallocated ageing report |
| `/api/v1/group-receipts/{id}` | GET | `group.receipt.capture` | Detail with allocation status |
| `/api/v1/group-payments` | POST | `group.payment.capture` | Supplier payment run |
| `/api/v1/group-payments/{id}/allocations` | POST | `group.payment.allocate` | Allocate to supplier invoices |
| `/api/v1/reports/consolidated/trial-balance` | GET | `group.report.consolidated` | Watermarked, with AsAt per contributor |
| `/api/v1/reports/consolidated/income-statement` | GET | `group.report.consolidated` | Inter-company eliminated |
| `/api/v1/reports/consolidated/balance-sheet` | GET | `group.report.consolidated` | Clearing accounts eliminated |

### Security / multi-company rules

| Rule | Source |
|---|---|
| CompanyLink must be Active with `SharedReceipting` scope | ADR-122, MULTI_COMPANY.md §11 |
| Bank account belongs to one company — that company posts bank side | ADR-105 |
| Each leg runs inside target company's DB only | ADR-099, ADR-116 |
| No command handler opens transactions against two databases | ADR-116, architecture test |
| Consolidated is read-only; no write path reaches it | ADR-106 |
| Stale contributors are named, not silently summed | ADR-119 |

### Synchronization / offline rules

| Rule | Source |
|---|---|
| Group receipt entities are registry-only, not replicated to company DBs | ADR-099 |
| Company-leg receipts replicate normally (StoreToCloud, AppendOnly) | ADR-006 |
| Saga legs dispatched through registry outbox | ADR-116 |
| Idempotent retry is always safe; recovery is retry, never re-key | ADR-104 |

### Licensing rules

| Rule | Source |
|---|---|
| Module entitlement flag: `group_receipting` | CLAUDE.md §8 |
| Usage counter: group receipts captured, allocations dispatched | CLAUDE.md §8 |
| Read-only mode: group receipt read endpoints stay writable | ADR-028 |

### New ADR required

None. All decisions are covered by existing ADRs (099, 104, 105, 106, 116, 016).

## Objective

Money arrives once and belongs to several companies — and those companies are separate databases
(ADR-099), so nothing here can be one transaction. This stage builds the registry container that
captures the money, the saga legs that turn allocations into real per-company receipts, the paired
clearing intents that keep every company's books balanced on their own, and the consolidated read model
that shows the group without ever posting to it.

## Deliverables

**Registry**
- `GroupReceipt` — captured amount, currency, tender, reference, receiving bank account (which belongs
  to one company), capture date, status (`Draft`, `PartiallyAllocated`, `Allocated`, `Reversed`).
  Immutable once fully allocated; a correction is a reversal (`CLAUDE.md` §7 rule 7).
- `GroupReceiptAllocation` — company, customer account, optional target invoices, amount, and its leg
  state (`Pending` → `Applied` | `Compensated`). Σ allocations ≤ captured amount, as a check constraint
  and in the aggregate. Each allocation is dispatched through 06d's `ISagaCoordinator` as an idempotent
  leg keyed by `(group_receipt_id, allocation_id)` (ADR-104, ADR-116).
- `GroupPaymentRun` / `GroupPaymentAllocation` — the same shape for money going out to a supplier that
  several companies owe.
- `InterCompanyClearingIntent` — immutable, carrying both legs and the group document that caused them.
  Settled only when both companies acknowledge (ADR-105).

**Application**
- `IGroupReceiptService` — capture, allocate, part-allocate, reverse. Allocation dispatches legs; it
  does not post. A retried call cannot double-post, and recovery from a company outage is retry, never
  re-key.
- **Per-company leg handler** — runs *inside* the target company's database: creates
  `finance.ar_receipts`, raises the financial event, posts through **that company's** posting rules. No
  module names a GL account (§7 rule 12).
- `IConsolidationService` — trial balance, income statement, balance sheet and age analysis assembled in
  the registry from each company's published period figures, inter-company clearing eliminated, marked
  `IsConsolidated`, carrying each contributor's `AsAt` and naming any that are stale (ADR-106, ADR-119).
- **Period close refuses over an outstanding inter-company intent**, and names it.

**Infrastructure / posting**
- New posting-rule keys: `GroupReceiptAllocated`, `InterCompanyClearingRaised`,
  `InterCompanySettled`, seeded with a default rule set.
- Inter-company clearing account per company pair direction, auto-created on first use from a template
  in the chart of accounts.
- `finance.ar_receipts` and `finance.ap_payments` gain `group_document_id` and `intent_id` (both
  nullable) — a receipt captured directly in one company is still a normal receipt with nulls there.
- Clearing lines carry the intent id, so an unmatched leg is identifiable by document rather than by
  guesswork.
- **The net-zero reconciliation is a scheduled job across databases**, run after every intent and on a
  schedule, with an alarm and a named owner — not a transactional assertion, because there is no longer
  one to make.

**API** — `/api/v1/group-receipts` (capture, allocate, reverse, list unallocated),
`/api/v1/group-payments`, `/api/v1/reports/consolidated/*`. Every consolidated response carries the
watermark field.

**Permissions** — `group.receipt.capture/allocate/reverse`, `group.report.consolidated`.

## Business rules

1. **A group receipt posts nothing.** Only a leg posts, and it posts inside one company's database.
2. Unallocated money sits on the group receipt, in nobody's ledger, and ages on an unallocated report.
3. The bank side posts in the company that owns the receiving bank account. Every cent allocated to a
   sister company creates a clearing pair, both legs in the same transaction (ADR-105).
4. Clearing balances across a group net to zero at every instant. There is a standing reconciliation
   report and a test that asserts it after a randomised sequence of allocations and reversals.
5. Allocating to an invoice cannot over-allocate it, across companies or within one.
6. A reversal reverses the **intent**, which dispatches reversing legs to every company involved. It
   never edits a posted journal and never reverses one leg on its own.
7. Consolidated output is labelled, read-only, and is not a statutory or tax document (ADR-106).
8. VAT is company-level, always. There is no group VAT return.

## Tests / acceptance

- The operator's example, end to end across three real databases: R9 000 captured, allocated R1 000 /
  R3 000 / R5 000, two against specific invoices, one on account. Three AR receipts exist in three
  databases, all three trial balances still balance, clearing nets to zero, and group exposure drops by
  exactly R9 000.
- **One company's database is stopped mid-allocation**: its leg stays `Pending`, the other two apply,
  the unapplied-legs report shows it with an age, and bringing the database back applies it exactly once
  with no operator action beyond a retry.
- A period close attempted with that leg outstanding is refused and names the intent.
- Capture R9 000, allocate R4 000, leave R5 000: nothing of the R5 000 appears in any company's ledger,
  and the unallocated report shows it with an age.
- Reverse a fully allocated group receipt: every leg reverses, no journal is edited, exposure returns.
- A retried allocation call with the same allocation id posts once; a leg that fails three times then
  succeeds applies once.
- Randomised property test: 200 allocations, reversals and injected leg failures across 3 databases —
  clearing nets to zero once every intent settles, and every unsettled intent is on the report.
- Consolidated income statement over three companies with inter-company trade eliminated equals the
  hand-computed figure in the test fixture.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `money-and-tax` and `multi-company-guard` run, findings closed
- [ ] Migration reversible, `Down` executed
- [ ] Posting rules registered and seeded; no GL account named outside the rules engine
- [ ] `docs/DATA_MODEL.md` §4f and §4l extended; replication registry updated
- [ ] The net-zero reconciliation job, the unapplied-legs report and their alarms exist and are seeded
- [ ] Seed: the three-company group with one shared customer, one group receipt allocated across all three

## Implementation evidence

### Files created/modified (2026-09-04)

| File | Status | Description |
|---|---|---|
| `src/VumaRetail.Domain/Registry/GroupReceiptEntities.cs` | NEW | `GroupReceipt`, `GroupReceiptAllocation`, `GroupPaymentRun`, `GroupPaymentAllocation`, `InterCompanyClearingIntent`, `InterCompanyClearingLeg` + status enums |
| `src/VumaRetail.Domain/Finance/ArReceipt.cs` | EXTENDED | Added nullable `GroupDocumentId`, `IntentId` + `RecordFromGroup()` factory |
| `src/VumaRetail.Domain/Finance/ApPayment.cs` | EXTENDED | Added nullable `GroupDocumentId`, `IntentId` + `RecordFromGroup()` factory |
| `src/VumaRetail.Application/Abstractions/Registry/GroupReceiptPorts.cs` | NEW | `IGroupReceiptRepository`, `IGroupReceiptService`, `IGroupPaymentService`, `IConsolidationService`, `ICompanyFanOut`, `ICompanyLinkGuard`, DTOs |
| `src/VumaRetail.Application/Registry/GroupReceiptCommands.cs` | NEW | Commands, queries, and handlers for capture/allocate/reverse/unallocated/detail |
| `src/VumaRetail.Application/Registry/Permissions/RegistryPermissions.cs` | EXTENDED | 6 new permission constants + descriptors |
| `src/VumaRetail.Infrastructure/Registry/GroupReceiptService.cs` | NEW | Service impl with saga dispatch + link guard |
| `src/VumaRetail.Infrastructure/Registry/GroupReceiptRepository.cs` | NEW | Registry-side persistence |
| `src/VumaRetail.Infrastructure/Registry/GroupReceiptLegHandler.cs` | NEW | Per-company leg handlers (resolve target DB, post through company rules) |
| `src/VumaRetail.Infrastructure/Registry/ConsolidationService.cs` | NEW | Fan-out read + assembly of consolidated reports + ICCLR elimination |
| `src/VumaRetail.Infrastructure/Registry/NetZeroReconciliationJob.cs` | NEW | Scheduled cross-DB reconciliation with alarm |
| `src/VumaRetail.Infrastructure/Persistence/VumaRegistryDbContext.cs` | EXTENDED | DbSets + EF configurations for all new entities |
| `src/VumaRetail.Infrastructure/DependencyInjection/PersistenceServiceCollectionExtensions.cs` | EXTENDED | DI registrations |
| `src/VumaRetail.Web/GroupReceiptEndpoints.cs` | NEW | `/api/v1/group-receipts` endpoints |
| `src/VumaRetail.Web/ConsolidationEndpoints.cs` | NEW | `/api/v1/reports/consolidated/*` endpoints |
| `src/VumaRetail.StoreServer/Program.cs` | EXTEND | Endpoint mapping |
| `tests/VumaRetail.UnitTests/Registry/GroupReceiptTests.cs` | NEW | Domain entity tests |
| `tests/VumaRetail.UnitTests/Registry/GroupReceiptApplicationTests.cs` | NEW | Application layer tests |
| `tests/VumaRetail.UnitTests/Registry/ClearingNetZeroPropertyTests.cs` | NEW | Property test + income statement elimination test |

### Verification status

- **Build:** UNVERIFIED — .NET 10.0 SDK required, only 9.0 available on this machine. Projects target `net10.0`.
- **Unit tests:** UNVERIFIED — same SDK limitation prevents `dotnet test`.
- **Integration tests:** UNVERIFIED — no PostgreSQL on this machine.
- **Migration Down:** UNVERIFIED — no PostgreSQL on this machine.

**Reason:** This session ran on Linux with .NET SDK 9.0.316 while all `.csproj` files target `net10.0`. The `global.json` requires SDK 10.0.400. All verification is deferred to a machine with the correct SDK.
