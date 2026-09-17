# TASK-29-005 — Durable report scheduling boundary

**Status:** COMPLETE for durable schedule slice · **Stage:** 29 · **Type:** Domain, persistence, API, migration, tests

Added a cloud-owned, tenant/company-scoped `ScheduledReport` record with bounded interval cadence,
UTC next-run timestamps, enable/disable state and missed-interval advancement. Added the published-
report-guarded `ScheduleReportCommand`, protected `POST /api/v1/reports/schedules` route, repository
queries for due schedules, PostgreSQL mapping and migration `Stage29ScheduledReports`.

Evidence: reporting unit suite **18/18 passed**; the PostgreSQL migration-chain test passes **1/1** and
verifies the schedule table is created and rolled back. A production polling worker still requires
provider-specific report data adapters and deployment scheduling configuration.
