# Stage 25 — HR Management

**Status:** IN_PROGRESS — employee, contract, leave, persistence, permissions and API foundations are implemented and tested; documents, disciplinary workflows, payroll export and full acceptance remain.

Owns tenant employees, employment terms and leave. All tables use `hr_management`; cross-module
references are identifiers or published contracts, never database foreign keys.

Completed: TASK-25-001 employee core, TASK-25-002 contracts, TASK-25-003 leave management.

Verification recorded 2026-09-13: `HrStagesRulesTests` (6/6) and `HrLifecycleTests` (4/4) pass. Remaining scope is tracked in the stage specification and is not marked complete until the document, disciplinary and payroll-export contracts have executable evidence.
