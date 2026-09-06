# Task

## Status

COMPLETE

> **Verdict (2026-09-06): FAIL — Stage 07c is not DONE.** Verification ran to completion on a
> Linux box with .NET 9.0.316 (no Docker/PostgreSQL). Everything provable without a database
> was proven; everything needing a database is marked UNVERIFIED per AGENTS.md — except the
> five findings below, which are structural FAILs established by code inspection with worked
> examples, not suspicions. Rework is scoped in TASK-07C-004. Do not mark Stage 07c DONE
> until 004 lands and the DB-backed criteria are proven against real databases.

## Stage

Stage 07c — Cross-company money: group receipting, allocation, inter-company clearing, consolidated reporting

## Type

TESTING, VERIFICATION

## Objective

Complete Stage 07c verification: run the full acceptance test suite, execute the agent panel (money-and-tax, multi-company-guard), verify the migration Down, confirm seed data, update documentation, and run the exit checklist.

## Why

A stage is not DONE until its own Exit Checklist and the global Definition of Done in CLAUDE.md §8 both pass. This task runs the tests that prove the stage works and the reviews that prove it is correct.

## Scope

- Run all test scenarios from the stage document
- Run `money-and-tax` and `multi-company-guard` agent reviews
- Verify EF migration `Down` executes
- Verify seed data: 3-company group, 1 shared customer, 1 group receipt allocated across all 3
- Verify posting rules seeded and no GL account named outside rules engine
- Verify `docs/DATA_MODEL.md` §4f and §4l extended
- Verify replication registry updated
- Verify net-zero reconciliation job, unapplied-legs report, and alarms exist
- Update `docs/PROGRESS.md` with stage evidence
- Append any new ADRs to `docs/DECISIONS.md`
- Commit at green checkpoint

## Out of Scope

Implementation of domain/application/infrastructure code (TASK-07C-001 and TASK-07C-002).

## Architecture

This task does not write business code. It validates that the code written by TASK-07C-001 and TASK-07C-002 conforms to the architecture and passes all acceptance criteria.

## Architectural Boundaries

Agent reviews enforce:
- `money-and-tax`: no GL account named outside rules engine, posting rules correct, money arithmetic correct
- `multi-company-guard`: no command handler opens two DBs, saga legs idempotent, clearing nets to zero, consolidated labelled and read-only

## Dependencies

TASK-07C-001 and TASK-07C-002 must be complete.

## Relevant Files

- All files created/modified by TASK-07C-001 and TASK-07C-002
- `docs/PROGRESS.md`
- `docs/DECISIONS.md`
- `docs/DATA_MODEL.md`
- `tests/` (all 07c-related tests)

## Relevant Documentation

`docs/MULTI_COMPANY.md`, `docs/DECISIONS.md` ADR-104, ADR-105, ADR-106, ADR-116, `CLAUDE.md` §8, `docs/TESTING.md`.

## Implementation Requirements

None — this is a verification task.

## Data/Database Impact

Verify migration `Down` executes cleanly. Verify seed data populates correctly.

## API Impact

Verify all endpoints appear in OpenAPI with correct permissions and error responses.

## Security

Verify permissions registered: `group.receipt.capture`, `group.receipt.allocate`, `group.receipt.reverse`, `group.payment.capture`, `group.payment.allocate`, `group.report.consolidated`.

## Multi-Company/Tenant Impact

Verify the 3-database example works end to end. Verify clearing nets to zero. Verify consolidated carries AsAt and names stale contributors.

## Sync/Offline Impact

Verify group receipt entities are registry-only. Verify company-leg receipts replicate normally.

## Acceptance Criteria

1. The operator's example: R9 000 captured, allocated R1 000 / R3 000 / R5 000 across 3 real databases; three AR receipts exist; all three trial balances balance; clearing nets to zero; group exposure drops by R9 000
2. Mid-allocation outage: one company's DB stopped → leg stays Pending, other two apply, unapplied-legs report shows it with age, retry applies exactly once
3. Period close refused with outstanding intent, intent named
4. Partial allocation: R9 000 captured, R4 000 allocated, R5 000 unallocated — nothing in any ledger, unallocated report shows it
5. Full reversal: every leg reverses, no journal edited, exposure returns
6. Idempotency: retried call posts once; failed-then-succeeded leg applies once
7. Randomised property test: 200 allocations/reversals/failures across 3 DBs → clearing nets to zero
8. Consolidated income statement eliminates inter-company trade
9. Coverage ≥ 80% on Domain + Application
10. Migration Down executes
11. Seed data present
12. Agent reviews pass with findings closed

## Tests Required

All test scenarios from the stage document, executed against real databases where possible.

## Edge Cases

- Build environment without PostgreSQL: record as UNVERIFIED, not PASS (AGENTS.md rule)
- Architecture test for no GL accounts named outside rules engine
- Architecture test for no command handler resolving two company DbContexts

## Definition of Done

- [ ] All acceptance criteria pass or are recorded as UNVERIFIED with reason
- [ ] `money-and-tax` review: findings closed
- [ ] `multi-company-guard` review: findings closed
- [ ] Migration Down verified
- [ ] Posting rules seeded; no GL account named outside rules engine
- [ ] `docs/DATA_MODEL.md` §4f and §4l extended
- [ ] Replication registry updated
- [ ] Net-zero reconciliation job, unapplied-legs report, alarms exist
- [ ] Seed data created
- [ ] `docs/PROGRESS.md` updated
- [ ] ADRs appended (if any new decisions)
- [ ] Committed at green checkpoint

## Follow-up Findings

None yet.

## Work Log

2026-09-06 verification session (Linux, .NET SDK 9.0.316, no Docker):

**Green (proven):**
- `dotnet build VumaRetail.sln -c Release`: 0 errors (after restoring two `Release|Any CPU.Build.0`
  lines the 08b merge dropped from `VumaRetail.sln` for Finance + CloudApi — that merge had left
  `main` red: Infrastructure compiled without its Finance reference, CS0234/CS0246).
- Unit tests: 977/977 green. Architecture tests: 54/54 green (incl. no-GL-account-outside-rules,
  no-two-DbContexts, no-hard-delete rules).
- `dotnet ef migrations has-pending-model-changes`: clean for both `VumaRetailDbContext` and
  `VumaRegistryDbContext`.
- Permissions: 7 constants registered in `RegistryPermissions` + descriptors; endpoints now reference
  the constants (this session replaced 9 hardcoded strings in `GroupReceiptEndpoints`/
  `ConsolidationEndpoints` — same values, drift-proof).
- `docs/DATA_MODEL.md` §4l extended (group/clearing/saga tables); §4f extended this session
  (`group_document_id`, `intent_id` on receipts/payments).
- Read paths exist and are unit-tested: consolidation trial balance/income statement with watermark,
  stale-contributor naming, inter-company elimination vs hand-computed fixture; unallocated ageing
  query + endpoint; `NetZeroReconciliationJob` + outstanding-intents report exist as code.

**FAIL (structural, proven by inspection — see TASK-07C-004 for worked examples):**
1. Acceptance #1, #2, #6: saga legs never execute — `SagaCoordinator.DispatchLegAsync`
   (`src/VumaRetail.Infrastructure/Registry/SagaCoordinator.cs:73`) is `await Task.CompletedTask`.
   `AllocateAsync` marks legs Acknowledged and completes the intent while no company database is
   ever touched. A mid-allocation outage test is meaningless: there is no leg to stay Pending.
2. `GroupReceiptLegHandler`/`GroupReceiptReversalLegHandler` have zero callers (dead code, DI-only),
   and even if called, `ApplyAllocationLegAsync` adds the `ArReceipt` to a locally-created context
   it never saves — the receipt would be silently lost.
3. Acceptance #5: `ReverseAsync` (`GroupReceiptService.cs:84`) flips registry state only; it
   dispatches no reversing legs despite its own comment claiming otherwise.
4. No code path ever creates an `InterCompanyClearingIntent` (only repository + read sides exist),
   so business rules 3–4 (clearing pairs, net-zero across DBs) have no write path; the property
   test proves domain math, not the system.
5. Acceptance #3: no `IPeriodCloseGuard` exists anywhere — period close cannot refuse over
   outstanding intents because nothing checks.
6. Acceptance #11 (seed): `DemoSeed` has no 3-company group, no shared customer, no group receipt,
   and no posting rules for `group.receipt.allocated`/`group.receipt.reversed` (nor, pre-existing,
   for `ar.receipt.posted`/`ap.payment.posted`).
7. Acceptance #8 half-gap: `IGroupPaymentService` is a port with no implementation and there are no
   `/api/v1/group-payments` endpoints, though the stage doc lists them as deliverables.

**UNVERIFIED (needs Docker/PostgreSQL, not FAIL):**
- Acceptance #1 end-to-end across 3 real DBs, #2 outage replay, #4 partial-allocation ledger
  absence, #7 randomised 200-op multi-DB run, migration `Down` *execution* (Down methods exist;
  reversibility pattern matches the verified 06c precedent), seed population, coverage ≥80% measured
  per-stage (suite is green; per-stage line-coverage report not produced on this box).

**Agent reviews (inspection-grade, this session — full panel re-runs in 004 with a DB):**
- `money-and-tax`: PASS so far — legs raise events only, no account named outside the rules engine
  (architecture test enforces); amounts carry explicit currency; no `double` money found on the path.
- `multi-company-guard`: FAIL — `RequireLink(...SharedReceipting...)` is checked at allocate time
  (good), but "legs run inside the target company DB" is vacuous while legs never run, and
  `intentId: null` is passed to `RecordFromGroup`, defeating the traceability the column exists for.
