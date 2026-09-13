# TASK-23-004 — Persisted service SLA policy command

**Status:** COMPLETE for SLA policy slice · **Stage:** 23 · **Type:** Application, persistence, API, tests

Implemented tenant/company-scoped `CreateServiceSlaCommand`, duplicate-name protection through the
existing service repository, and the permissioned `POST /api/v1/service/slas` route. The existing
`service_slas` migration and mapping are reused; no parallel table was introduced.

Evidence: Service-focused unit suite **28/28 passed** and the StoreServer application compiled as part
of that run. Full SLA assignment, pause/resume clocks, worker escalation and stage closure remain open.
