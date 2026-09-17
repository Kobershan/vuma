# TASK-23-004 — Persisted service SLA policy command

**Status:** COMPLETE and verified · **Stage:** 23 · **Type:** Application, persistence, API, tests

Implemented tenant/company-scoped `CreateServiceSlaCommand`, duplicate-name protection through the
existing service repository, and the permissioned `POST /api/v1/service/slas` route. The existing
`service_slas` migration and mapping are reused; no parallel table was introduced.

Evidence: Service-focused unit suite **28/28 passed**, StoreServer compiles, persisted breach audits
are migration-tested, and Stage 23 closure verification is recorded in
`docs/verification/STAGE-23-VERIFICATION.md`.
The SLA creation handler now also enforces the ambient active-company context before duplicate lookup
or persistence; the focused service-domain suite passes **6/6**.
