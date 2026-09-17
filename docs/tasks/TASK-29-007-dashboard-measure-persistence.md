# TASK-29-007 — Persisted dashboard aggregate measures

**Status:** COMPLETE for measure persistence/read slice · **Stage:** 29 · **Type:** Domain, application, infrastructure, migration, tests

Added company/date/name/currency-scoped `DashboardMeasure` records with monotonic replacement,
currency-preserving identity and Store-to-Cloud replication metadata. The dashboard overview now reads
persisted measures and exposes keys as `NAME|CURRENCY`, preventing cross-currency aggregation. Added
the reporting mapping, repository read/write ports and reversible `Stage29DashboardMeasures` migration.

Evidence: reporting unit suite **20/20 passed**; PostgreSQL migration-chain test passes **1/1** with
creation and rollback of `dashboard_measures`. Source-module projection adapters and full KPI
acceptance remain open.
