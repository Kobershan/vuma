# TASK-26-003 — Workforce availability read

Status: COMPLETE for availability-read slice  
Stage: 26  
Type: Application/API

## Objective

Provide a permissioned availability read that accounts for the employee lifecycle state and
overlapping scheduled work in a requested time window.

## Scope

Application query/handler, workforce API route, cancellation-aware shift filtering and unit tests.
Shift swaps, labour-cost integration, database concurrency guarantees and compliance review remain
follow-up work.

## Security and tenancy

The route is protected by `workforce.view`; employee and shift repositories apply the tenant boundary
already used by the HR/workforce persistence registrations.

## Acceptance and verification

- Invalid time windows are rejected.
- Missing employees are rejected.
- Cancelled shifts do not block availability.
- Inactive employees and overlapping non-cancelled shifts are unavailable.

2026-09-13: HR unit suite passes 11/11, including the availability query; StoreServer Release build
passes with 0 errors.

## Follow-up findings

Roster publication, shift swaps, labour-cost-versus-sales reporting and specialist/runtime review
remain open for Stage 26.
