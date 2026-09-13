# TASK-26-004 — Shift-swap state

Status: IN_PROGRESS  
Stage: 26  
Type: Domain

## Objective

Represent a shift-transfer request without mutating the scheduled shift until an authorized decision
is made.

## Scope

Two-employee identity validation and pending → approved/rejected state transitions. The request and
decision APIs use the workforce manage permission, and requests are persisted in the tenant-scoped
`hr_workforce.shift_swap_requests` table by migration `20260913220207_Stage26ShiftSwapPersistence`.
An approved request now transfers the planned shift only after target-employee overlap and original
owner checks. Roster publication, labour-cost integration and specialist review remain follow-up
work.

## Verification

2026-09-13: HR lifecycle unit tests pass 10/10, covering same-employee refusal, decide-once behavior,
handler ownership validation, persistence registration, target conflict checks, and decision application. StoreServer
Release build passes with 0 errors.
