# TASK-29-003 — Durable report export requests

**Status:** COMPLETE for export-request slice · **Stage:** 29 · **Type:** Domain, persistence, API, tests

Added a tenant/company-scoped, idempotent `ReportExport` request record with queued/completed/failed
states, a unique operation identity, and `POST /api/v1/reports/exports` plus status retrieval. The
request is rejected unless the report definition is published.

Evidence: reporting unit tests **6/6 passed**; PostgreSQL migration Up/Down chain **1/1 passed**;
`report_exports` is removed independently before the existing reporting tables are rolled back.
Worker execution, file storage, scheduling and full export authorization acceptance remain open.

2026-09-13: Completion/failure commands now provide the worker handoff boundary, enforce the active
company, and persist an artifact reference for completed exports. Export requests now require the
high-risk `reporting.report.manage` permission; `ReportingDomainTests` passes 8/8. Migration
`Stage29ReportExportArtifacts` adds the durable artifact reference. Actual renderer, storage adapter,
scheduled worker and expiring download authorization remain open.

The status query additionally requires the active company to match the export company; a regression
test prevents same-tenant cross-company status disclosure.
