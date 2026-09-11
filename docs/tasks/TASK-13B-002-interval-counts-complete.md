# TASK-13B-002 — Interval counts and complete Stage 13b

**Status:** COMPLETE (2026-09-11) · **Stage:** 13b · **Type:** Build + verification
**Depends on:** TASK-13B-001.
**Reference reading:** TASK-13B-001; STAGE-13b-picking-waves-staging.md; ADR-115.

## Objective

Complete interval counts with slow-mover selection and in-flight warnings. Add the handheld flow update so a picker's confirm writes the staging move. Run specialist reviews, verify exit checklist, update docs.

## Scope

### Application extension

- `CountSchedule` generates Stage 13 `CycleCount`s via the existing `ICycleCountService`.
- Slow-mover selector: items with no movement in N days, weighted by last-touch, plus random sample.
- In-flight warning: when a count line's SKU has quantity in a Consolidation/Packing/Dispatch bin, the count sheet says so with quantity and wave/shipment reference.
- Variance computed against on-hand including staging, not shelf alone.
- A schedule never selects an item currently being picked for a shelf bin that wave draws from.

### Infrastructure

- Quartz job: `CountScheduleExpirationHostedService` runs daily.
- Extend warehouse repositories for the new entity types.
- Seed: a Durban wave, picked into consolidation, one interval count with an in-flight warning.

### Verification

- `stock-availability-guard`, `architecture-guard`, `sync-and-offline` run.
- Migration reversible, Down executed.
- `docs/DATA_MODEL.md` §4n complete; replication registry updated.
- Seed proven; OpenAPI presence confirmed.
- Coverage ≥ 80% on the stage's Domain + Application (combined with 001).
- Stage doc marked COMPLETE.

## Out of Scope

- Till UI, Android app, second counting model, new pick task state machine.

## Verification update (2026-09-11)

Actual bin-stock snapshots and in-flight warnings are covered by the warehouse unit suite (75/75),
the daily schedule runner is registered and advances due schedules by cadence, and the real PostgreSQL
warehouse/cross-company suites are green. The direct Stage 13b migration Up/Down check passes, and
the full repository gates are green. Slow-mover selection uses last-movement activity plus a
deterministic sample and is covered by the warehouse tests.
