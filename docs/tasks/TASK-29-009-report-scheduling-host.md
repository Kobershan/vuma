# TASK-29-009 — Hosted report scheduling and export execution

**Stage:** 29 — Reporting & Admin Dashboard API
**Status:** COMPLETE (2026-09-18)

## Scope

Register durable report scheduling in StoreServer and execute queued exports for active companies
without crossing tenant or company boundaries. The worker uses a periodic timer, creates a fresh
scope for each company, advances due schedules, commits the enqueue boundary, renders queued
exports through the configured data source and artifact store, and commits each completion/failure.

## Implementation

- `ReportSchedulingHostedService` discovers active registry companies under the installation tenant.
- `ListQueuedExportsAsync` reads bounded, FIFO queued exports for the active company.
- `AddVumaReportingScheduling` registers options, host identity and the hosted service.
- StoreServer registers reporting scheduling next to `AddVumaReporting`.
- Export failures are persisted and logged while the sweep continues with later exports and companies.

## Verification

- StoreServer Release build: passed with 0 errors.
- Reporting unit tests: **21/21**.
- Architecture rules: **86/86**.
- Workflow host/API integration check: **11/11**.

Production object storage, provider-specific source adapters and external acceptance remain separate
deployment work; this task supplies the repository-owned hosted scheduling boundary.
