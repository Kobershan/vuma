# Stage 25 — HR Management

**Status:** COMPLETE for the repository-owned payroll boundary (2026-09-20) — employee, contract, leave, employee-document metadata, disciplinary workflows, source payroll export and durable payroll runs are implemented and tested. Statutory-provider integration, live payroll acceptance and specialist review remain external/configuration boundaries.

Owns tenant employees, employment terms and leave. All tables use `hr_management`; cross-module
references are identifiers or published contracts, never database foreign keys.

Completed: TASK-25-001 employee core, TASK-25-002 contracts, TASK-25-003 leave management.

Verification recorded 2026-09-13: `HrStagesRulesTests` (6/6), `HrLifecycleTests` (5/5), and `EmployeeDocumentTests` (4/4) pass. Employee documents store only validated external blob metadata and a checksum. Suspend, activate and terminate employee lifecycle commands/routes are now implemented. Company-scoped disciplinary-case persistence, commands, listing query and HR-protected routes use the dedicated high-risk `hr.disciplinary.manage` permission and are covered by `DisciplinaryCaseTests` (4/4). Source-only payroll export is protected by `hr.payroll.export`, available as JSON and deterministic CSV, and covered by `PayrollExportTests` (3/3); durable payroll delivery, statutory integration, acceptance and specialist review remain.

Verification recorded 2026-09-14: employee lifecycle, leave decision and shift-swap decision handlers
validate loaded tenant scope before mutation. HR-focused tests pass **102/102**.

Verification recorded 2026-09-20: durable company-scoped payroll runs and lines support idempotent
request replay, attendance/contract-derived gross calculation, explicit currency, deductions and
finalization. StoreServer Release build succeeds; payroll unit tests pass **2/2**. The API exposes
`POST /api/v1/hr/payroll/runs` and `/api/v1/hr/payroll/runs/{id}/finalize`. Statutory tax,
bank/payment-provider delivery and live payroll acceptance remain external acceptance items.
