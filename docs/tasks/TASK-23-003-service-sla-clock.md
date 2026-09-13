# TASK-23-003 — Deterministic service SLA clock

**Status:** COMPLETE for the clock slice; worker and pause policy remain open · **Stage:** 23 · **Type:** Application, tests

## Scope

Provide an injectable service SLA clock that counts configured weekday business hours in UTC. The
clock is pure and deterministic; callers can subtract explicitly audited waiting intervals before
evaluating a ticket deadline.

## Evidence

- `IServiceSlaClock` and `BusinessHoursServiceSlaClock` are implemented in
  `src/VumaRetail.Application/Service/ServicePorts.cs`.
- `ServiceSlaClockTests`: **2/2 passed**.
- The infrastructure module registers the default 09:00–17:00 UTC clock through dependency injection.
- Remaining Stage 23 work: persisted business calendars, pause/resume custody events, SLA worker,
  and full API/isolation acceptance.
