# TASK-23-004 — Service SLA deadline calculation

Status: IN_PROGRESS  
Stage: 23  
Type: Application

## Objective

Extend the service SLA calendar port with deterministic working-hour deadline calculation.

## Scope

UTC weekday business-hour arithmetic, forward deadline calculation across closing times and
weekends, and refusal of negative durations. Ticket pause accounting and hosted worker execution
remain separate follow-up work.

## Verification

2026-09-13: `ServiceSlaClockTests` passes 4/4, including Friday-to-Monday deadline calculation and
negative-duration validation. The existing StoreServer build path compiled the changed application
port successfully.

## Follow-up findings

Waiting-for-customer pause accounting, SLA breach persistence/worker execution, and full PostgreSQL
acceptance remain open.
