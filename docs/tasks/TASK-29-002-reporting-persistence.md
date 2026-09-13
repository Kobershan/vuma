# TASK-29-002 — Reporting definitions and checkpoints persistence

**Status:** COMPLETE for persistence and definition-read slice · **Stage:** 29 · **Type:** Application, infrastructure, migration, API

Persisted `ReportDefinition` and `ProjectionCheckpoint` under the `reporting` schema, added scoped
repository ports/implementation and registered them in StoreServer and CloudApi. Checkpoints remain
monotonic in generation/cursor order and are suitable for replay-safe projection workers.

The `reporting.view` permission and `GET /api/v1/reports/{code}` definition-read route are also
registered on both hosts.

Evidence: the PostgreSQL migration-chain test passes through Stage 29, verifies both reporting tables,
and rolls back cleanly to Stage 28. Projection adapters, dashboard queries, export jobs and API
acceptance remain open.
