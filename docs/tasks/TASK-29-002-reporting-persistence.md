# TASK-29-002 — Reporting definitions and checkpoints persistence

**Status:** COMPLETE for persistence and definition-read slice · **Stage:** 29 · **Type:** Application, infrastructure, migration, API

Persisted `ReportDefinition` and `ProjectionCheckpoint` under the `reporting` schema, added scoped
repository ports/implementation and registered them in StoreServer and CloudApi. Checkpoints remain
monotonic in generation/cursor order and are suitable for replay-safe projection workers.

The `reporting.view` permission and `GET /api/v1/reports/{code}` definition-read route are also
registered on both hosts.

Evidence: the PostgreSQL migration-chain test passes through Stage 29, verifies the reporting tables,
and rolls back cleanly to Stage 28. Durable export requests and status routes are covered by
TASK-29-003; projection adapters, dashboard queries, export worker execution and scheduling remain open.
