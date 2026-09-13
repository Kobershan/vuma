# TASK-25-005 — Employee lifecycle operations

Status: IN_PROGRESS  
Stage: 25  
Type: Application/API

## Objective

Expose the employee suspension, reactivation and termination transitions through tenant-scoped
commands and HR management routes.

## Scope

`Employee` lifecycle transitions, dispatcher handlers, HR routes, permission enforcement and unit
regression coverage. Disciplinary workflows and payroll export are follow-up work.

## Security and tenancy

All mutations use the existing HR manage permission and the existing employee repository boundary;
employee lookup remains tenant-scoped by the registered persistence implementation.

## Acceptance criteria

- Suspend, activate and terminate commands are registered through the application dispatcher.
- HR API exposes POST routes for all three transitions.
- Termination rejects a date before hiring and records `TerminatedAt`.
- Unit lifecycle coverage passes.

## Verification

2026-09-13: StoreServer Release build passes with 0 errors; HR unit suite passes 9/9, including the
suspend → activate → terminate transition and the existing HR rules, leave, shift, attendance and
document tests.

## Follow-up findings

Disciplinary case management, payroll export and specialist/runtime review remain open at Stage 25.

2026-09-13: Added the disciplinary-case domain state machine as the foundation for the follow-up
workflow. `DisciplinaryCaseTests` passes 2/2, covering investigation-before-decision, event ordering,
and one-way decision state. Persistence, commands/routes, payroll export and specialist/runtime review
remain open.

2026-09-13: Disciplinary cases are now company-scoped and persisted through
`Stage25DisciplinaryCases`; open, investigate and decide commands/routes are wired behind HR manage
the dedicated high-risk `hr.disciplinary.manage` permission, and company-scoped listing is available behind HR view permission. `DisciplinaryCaseTests`
passes 4/4 and the StoreServer build passes with 0 errors. Payroll export, acceptance and
specialist/runtime review remain open.
