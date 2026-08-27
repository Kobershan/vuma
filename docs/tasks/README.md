# TASKS — Vuma Retail

Tasks are the normal implementation unit. A task must be small enough for one focused session to
discover, plan, implement, test, review, and document without loading the whole repository.

## Naming and status

Use `TASK-NNN-short-name.md`. Status is one of `NOT_STARTED`, `IN_PROGRESS`, `BLOCKED`, or
`COMPLETE`. Only one task may be `IN_PROGRESS` at a time. `docs/CURRENT.md` points to it.

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

Every task contains: objective, why, scope, out of scope, affected files, dependencies, relevant
references, implementation requirements, acceptance criteria, tests, edge cases, security
considerations, architectural decision impact, definition of done, status, and a short work log.

## Session rule

Read `CLAUDE.md`, `docs/CURRENT.md`, the relevant roadmap section, the current stage, this task, and
only the referenced source/tests. At handoff, update the task and `docs/CURRENT.md`; put historical
evidence in `docs/PROGRESS.md`.
