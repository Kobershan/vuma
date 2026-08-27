# SESSION KICKOFF — Vuma Retail

Use `/next-stage` in a fresh session. The command name is retained for compatibility; it now selects
the single task named by `docs/CURRENT.md`, not an entire stage.

If slash commands are unavailable, use this compact instruction:

```text
Read CLAUDE.md, docs/CURRENT.md, the relevant section of docs/ROADMAP.md, the current stage document,
and the current task under docs/tasks/. Read only that task's references and relevant source/tests.
Work one task through DISCOVER → PLAN → IMPLEMENT → TEST → REVIEW → DOCUMENT → COMPLETE. Do not expand
scope or start another task. Run applicable tests and reviews, inspect the diff, update the task and
docs/CURRENT.md, record material history in docs/PROGRESS.md, add an ADR for durable decisions, and
stop at a green checkpoint. Mark unavailable checks UNVERIFIED with the required environment.
```

## Startup checklist

1. Read `CLAUDE.md`.
2. Read `docs/CURRENT.md`.
3. Identify the current stage and task.
4. Read the task and only its relevant references, source, and tests.
5. Confirm dependencies and the task boundary.
6. Plan, implement, test, review, and document one task.

## Handoff checklist

Update the task and `docs/CURRENT.md` with the completed work, changed files, tests and results,
important decisions, blockers, and the next task. Put durable historical evidence in
`docs/PROGRESS.md`; do not copy the conversation transcript there.

## Compaction rule

When context becomes large, stop at a green checkpoint. Preserve only the task objective, completed
requirements, files changed, decisions, tests/results, known issues, blockers, and next action in the
task and current-state files. The next session starts from those files.

## Stage completion

A stage is complete only after all required task files are complete, the stage exit checklist passes,
the required specialist panel runs, and the stage-level documentation is updated. A task completion
does not imply stage completion.
