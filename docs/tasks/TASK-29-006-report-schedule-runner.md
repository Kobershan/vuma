# TASK-29-006 — Idempotent due-schedule enqueue boundary

**Status:** COMPLETE for enqueue boundary · **Stage:** 29 · **Type:** Application, tests

Added `IReportScheduleRunner` and `ReportScheduleRunner`, which bounds a due-schedule pass, derives a
stable operation identity from schedule/run time, enqueues at most one export request per scheduled
run, skips retired definitions, and advances missed intervals beyond the supplied UTC watermark.
The runner leaves transaction ownership with the host and rendering with `ReportExportExecutor`.

Evidence: reporting unit suite **19/19 passed**. A hosted timer/polling registration and provider data
adapters remain deployment-specific integration work.
