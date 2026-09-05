# Task

## Status

NOT_STARTED

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

Not started.
