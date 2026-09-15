# TASK-23-004 — Service SLA deadline calculation

Status: COMPLETE for deadline/pause-accounting slice — breach worker and acceptance remain a Stage 23 follow-up
Stage: 23  
Type: Application

## Objective

Extend the service SLA calendar port with deterministic working-hour deadline calculation.

## Scope

UTC weekday business-hour arithmetic, forward deadline calculation across closing times and
weekends, refusal of negative durations, and explicit waiting-for-customer pause accounting.

## Verification

2026-09-13: `ServiceSlaClockTests` passes 6/6, including Friday-to-Monday deadline calculation,
negative-duration validation, and a waiting-for-customer pause. Service tickets now persist the
pause start and accumulated working hours; `POST /api/v1/service/tickets/{id}/resume` closes the
pause using the configured business-hours clock. Migration `Stage23CustomerWaitPause` adds the
durable fields.

## Follow-up findings

SLA breach persistence/worker execution and full PostgreSQL acceptance remain open.
