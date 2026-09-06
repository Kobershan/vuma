# Task

## Status

NOT_STARTED

## Stage

Stage 08c — Cross-company availability, reservations & split fulfilment

## Type

VERIFICATION, TESTING, DOCUMENTATION

## Objective

Prove Stage 08c against real, concurrent database transactions — not mocks — for every
acceptance criterion in the stage document, measure the ≥ 80% coverage on the stage's Domain
+ Application, execute the migration `Down`, run the stage's specialist guards, and close the
exit checklist honestly (UNVERIFIED with a named environment where something genuinely cannot
run here).

## Why

The stage's one non-negotiable rule (available never goes negative under any interleaving,
including stale projections) is a concurrency property. A mock cannot fail the way two
serialisable transactions fail — ADR-101's credit-hold test states the same principle for its
own race, and this task applies it to all of 08c's races. Verification is therefore its own
task with its own bar, not a checkbox on the implementation tasks.

## Scope

- Re-run the FULL acceptance list from `docs/stages/STAGE-08c-availability-sourcing.md`
  against real PostgreSQL (`VUMA_TEST_POSTGRES`, local PG18; Testcontainers if Docker is
  present — never skip):
  1. 20 demanded / A=12 + B=30 → 12+8, no backorder, B ≥ 0.
  2. 20 demanded / A=12 + B=3 → 15 held, 5 backordered, nothing negative.
  3. Two concurrent orders, last 5 units, one company → 5 + backorder-5, genuinely parallel
     transactions (barrier start, two connections, assert exactly one winner and zero
     negatives; run 20 iterations to catch flake).
  4. Stale projection (B shown 30, really 3) → 3 held, rest backordered, never negative.
  5. Two-company commit, second leg fails → Release-row compensation, no order, both
     companies' availability exactly where it started (compare full before/after snapshots).
  6. Reserve → release → reserve → availability identical, three ledger rows.
  7. Rebuild-from-scratch equals incremental projection after 500 random movements and
     reservations (seeded RNG, fixed seed committed in the test so failures reproduce).
  8. Committed two-company plan → two documents whose lines sum to the order.
  9. Coverage ≥ 80% on the stage's Domain + Application (coverlet, stage-scoped filter —
     follow the precedent Stage 14's 91.3% measurement set, record the exact command).
- Migration reversibility: `Up` then `Down` then `Up` on a scratch database for every 08c
  migration (company + registry chains); assert the schema diff is empty afterwards
  (`dotnet ef migrations has-pending-model-changes` for both contexts).
- Specialist review pass (as checklists against the built code — this machine has no agent
  runner; each finding closed or recorded with a reason in PROGRESS.md):
  `stock-availability-guard` (reservations, ATP, staging states), `multi-company-guard`
  (MULTI_COMPANY.md §11's twelve rules), `architecture-guard` (references, boundaries,
  handlers). Then `stage-verifier` semantics: exit checklist item by item.
- Documentation: `docs/DATA_MODEL.md` §4m (new: `stock_reservations`, `available_balances`,
  `group_availability_rows`, `reservation_expiry_policies`, `sales_orders.group_document_ref`
  — with the same column/index/constraint detail §4e uses); replication-registry row for
  `StockReservation` (StoreToCloud/AppendOnly) in `docs/SYNC_AND_BACKUP.md` §3's entity table
  (verify the table exists; if the registry lives elsewhere, put the row where it lives and
  say so in the Work Log); seed present and exercised by at least one test.
- `docs/PROGRESS.md` stage entry + `docs/CURRENT.md` handoff; commit at the green checkpoint.
  No new ADR expected; if verification forces a design change, the ADR goes through the normal
  append path and the implementation tasks re-open.

## Out of Scope

- Fixing anything the verification finds — findings re-open TASK-08C-001/002 (recorded as
  such); this task does not silently become a fourth implementation task.
- Windows-only checks (FlaUI, Velopack, WiX): not applicable to this stage; recorded as N/A,
  not UNVERIFIED.
- 07C-004, Stage 14 rework, or any other stage's criteria.

## Architecture

No new code beyond test/seed/doc fixes. If a test needs production code to change, that
change belongs to 001/002 with its own test — this task holds the line on that.

## Architectural Boundaries

Verification-only: no production-code restructuring to make numbers pass. A coverage gap is
closed by testing untested behaviour, not by excluding files from measurement.

## Dependencies

TASK-08C-001, TASK-08C-002 (both COMPLETE, main green).

## Relevant Files

- `docs/stages/STAGE-08c-availability-sourcing.md` (acceptance list + exit checklist)
- `tests/VumaRetail.IntegrationTests/Inventory/`, `tests/VumaRetail.UnitTests/Inventory/`
  (plus the 08c additions from 001/002)
- `docs/DATA_MODEL.md` §4e (style precedent) + §5 replication registry,
  `docs/SYNC_AND_BACKUP.md` §3, `docs/PROGRESS.md`, `docs/CURRENT.md`
- `scripts/pg-test.sh` (throwaway cluster if `VUMA_TEST_POSTGRES` is unset)

## Relevant Documentation

Stage doc, `docs/TESTING.md` (ratio + matrix), `docs/AGENTS.md` (review sequence),
`CLAUDE.md` §8 (Definition of Done), `docs/EXECUTION_STANDARD.md` if referenced by the stage.

## Implementation Requirements

- Every DB-backed criterion runs on PostgreSQL reachable from this machine; the exact server
  (local PG18 vs Testcontainers vs CI service container) is recorded in the Work Log.
- Concurrency tests assert *invariants* (sums, non-negativity, exactly-one-winner), never
  timing ("both finished within Ns").
- The 500-op rebuild test uses a fixed RNG seed; the test name states it.
- Coverage measured with the repo's existing mechanism only (no new tooling to make a number).

## Data/Database Impact

None (scratch databases only). `Down` execution is the one deliberate schema mutation, on a
disposable database.

## API Impact

None — but the verification confirms the 001/002 endpoints appear in `/openapi/v1.json` with
examples and error responses (DoD item, checked not assumed).

## Security

Confirm the new endpoints' permission gates with at least one 403-permission test each
(unauthorised caller refused, authorised caller served) — R6-adjacent hygiene for new surface.

## Multi-Company/Tenant Impact

Criterion 5's before/after snapshot comparison IS the multi-company proof. Tenant isolation
(reservations invisible across tenants) re-confirmed by the 001 harness test.

## Sync/Offline Impact

Confirm the projection-publish path end to end at least once: reserve in a company DB →
event applied to registry projection → group read reflects it with a fresh `AsAt`; then the
rebuild-equality proof (criterion 7) covers the steady state.

## Acceptance Criteria

The stage's eight numbered criteria above (1–8) all PASS on real PostgreSQL, plus coverage
(9), `Down` executed (10), seed exercised (11), reviews closed (12) — mirroring TASK-07C-003's
verdict format (PASS/FAIL/UNVERIFIED per criterion with evidence, not a blanket "done").

## Tests Required

This task writes the verification-only tests missing from 001/002 (iteration loops, snapshot
comparisons, OpenAPI presence, permission 403s) and runs everything.

## Edge Cases

- Flaky-passing concurrency tests: 20-iteration loop with per-iteration fresh database; any
  single failure is a FAIL, not "usually passes".
- Clock-sensitive expiry test uses the controllable test clock, never wall-clock sleeps.

## Definition of Done

- [ ] All eight acceptance criteria PASS against real PostgreSQL (evidence in Work Log)
- [ ] Coverage ≥ 80% measured on the stage's Domain + Application (command recorded)
- [ ] Migration `Down` executed on scratch DB, diff empty, both contexts clean
- [ ] `stock-availability-guard`, `multi-company-guard`, `architecture-guard` findings closed
        or recorded with reasons; `stage-verifier` semantics applied to the exit checklist
- [ ] `docs/DATA_MODEL.md` §4m written; replication registry updated; seed exercised by a test
- [ ] `docs/PROGRESS.md` + `docs/CURRENT.md` updated; committed and pushed
- [ ] `CLAUDE.md` §8 followed in full

## Follow-up Findings

None yet — this task produces them.

## Work Log

Not started.
