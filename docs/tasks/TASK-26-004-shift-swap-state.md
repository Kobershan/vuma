# TASK-26-004 — Shift-swap state

Status: IN_PROGRESS  
Stage: 26  
Type: Domain

## Objective

Represent a shift-transfer request without mutating the scheduled shift until an authorized decision
is made.

## Scope

Two-employee identity validation and pending → approved/rejected state transitions. Persistence,
authorization, applying an approved swap to a roster, conflict rechecking and API wiring remain
follow-up work.

## Verification

2026-09-13: HR lifecycle unit tests pass 7/7, covering same-employee refusal and decide-once
behavior.
