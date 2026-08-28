# TASKS — Vuma Retail

Tasks are the normal implementation unit. A task must be small enough for one focused session to
discover, plan, implement, test, review, and document without loading the whole repository.

## Naming and status

Use `TASK-<STAGE>-<NUMBER>-<name>.md`, for example `TASK-06C-01-registry-foundation.md`.
Canonical status is one of `NOT_STARTED`, `READY`, `IN_PROGRESS`, `BLOCKED`, `COMPLETE`, or
`NEEDS_VERIFICATION`. `READY` is derived only when every listed dependency is `COMPLETE`. Only one
task may be `IN_PROGRESS` unless the stage index explicitly marks parallel work safe.

Stage documents remain the authority for stage scope and acceptance. Their task indexes identify
the tasks that make up the stage; task files contain the implementation detail for one unit.

## Task lifecycle

```text
DISCOVER → PLAN → IMPLEMENT → TEST → REVIEW → DOCUMENT → COMPLETE
```

An agent must not mark a task complete with known relevant failures. Environment limitations are
recorded explicitly as `UNVERIFIED — needs <environment>`.

## Scope control

Work only within the task's `Scope`. Anything unrelated becomes a follow-up with:

```text
FOUND:
WHY IT MATTERS:
RECOMMENDED FOLLOW-UP:
PRIORITY:
```

Do not silently refactor, repair, or redesign unrelated code.

## Required task template

Every canonical task contains the full execution template: `Status`, `Stage`, `Type`, `Objective`,
`Why`, `Scope`, `Out of Scope`, `Architecture`, `Architectural Boundaries`, `Dependencies`,
`Relevant Files`, `Relevant Documentation`, `Implementation Requirements`, `Data/Database Impact`,
`API Impact`, `Security`, `Multi-Company/Tenant Impact`, `Sync/Offline Impact`, `Acceptance Criteria`,
`Tests Required`, `Edge Cases`, `Definition of Done`, `Follow-up Findings`, and `Work Log`.

Historical `TASK-NNN-*` files remain preserved as legacy records; they are not queue entries unless a
canonical stage index links them.

## Session rule

Read `CLAUDE.md`, `docs/CURRENT.md`, the relevant roadmap section, the current stage, this task, and
only the referenced source/tests. At handoff, update the task and `docs/CURRENT.md`; put historical
evidence in `docs/PROGRESS.md`.
