# TASK-23-003 — Deterministic service SLA clock

**Status:** COMPLETE for clock, pause-accounting, and bounded worker slice; persistence acceptance remains · **Stage:** 23 · **Type:** Application, tests

## Scope

Provide an injectable service SLA clock that counts configured weekday business hours in UTC. The
clock is pure and deterministic; callers can subtract explicitly audited waiting intervals before
evaluating a ticket deadline.

## Evidence

- `IServiceSlaClock` and `BusinessHoursServiceSlaClock` are implemented in
  `src/VumaRetail.Application/Service/ServicePorts.cs`.
- `ServiceSlaClockTests`: **2/2 passed**.
- The infrastructure module registers the default 09:00–17:00 UTC clock through dependency injection.
- `IServiceSlaWorker` evaluates tenant/company-scoped open-ticket breaches using the same clock and
  persisted customer-wait pause accounting; `ServiceSlaClockTests` passes **7/7**.
- 2026-09-17: The service-filtered unit suite passes **40/40** after the custody isolation gate was
  added; no SLA clock regression was introduced.
- Remaining Stage 23 work: persisted business calendars and full API/isolation acceptance; breach
  observations are now durable append-only records with tenant/ticket/SLA/type uniqueness.
