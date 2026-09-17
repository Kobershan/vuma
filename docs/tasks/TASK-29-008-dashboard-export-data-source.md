# TASK-29-008 — Dashboard-backed export data source

**Status:** COMPLETE for persisted dashboard export slice · **Stage:** 29 · **Type:** Infrastructure, tests

Registered a scoped `DashboardReportDataSource` so queued report execution has a real provider rather
than an unresolved dependency. It reads the requested business-date dashboard measures, preserves
currency as a separate column, and feeds the deterministic CSV renderer. This also removes the host
validation failure that previously affected unrelated integration fixtures when `ReportExportExecutor`
was registered.

Evidence: reporting unit suite **21/21 passed**, workflow host/API checks **11/11 passed**, and the
full PostgreSQL integration suite **632/632 passed**. Provider-specific source adapters and production
object storage remain open.
