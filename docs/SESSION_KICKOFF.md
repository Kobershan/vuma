# SESSION KICKOFF — what to paste into a fresh chat

One command starts a session, and it is the same command every time. It lives in the repository as a
slash command so you do not have to keep the text anywhere:

```
/next-stage
```

That is the whole thing. It is defined in `.claude/commands/next-stage.md`, and it tells the session
to read the contract, pick the next incomplete stage, write the stage document if it is missing,
build it to completion, run the agent panel, update the markdown, commit and push.

## If the slash command is not available

Some hosts (headless runs, the web client, an older CLI) do not load project commands. Paste this
instead — it is the same instruction, compressed:

```
Build one stage of Vuma Retail, start to finish, in this session. Work these phases in order and
tick each as you go. Never ask me a question — decide, write an ADR, continue. Never leave the repo
broken. Verify by executing, never by asserting.

1. READ: CLAUDE.md in full. docs/EXECUTION_STANDARD.md in full. docs/PROGRESS.md §1 and §5 (§5 names
   the stage and wins over everything). docs/ROADMAP.md. docs/DECISIONS.md (LOCKED means settled).
2. CHOOSE: the first NOT_STARTED/IN_PROGRESS stage whose dependencies are all DONE, unless §5 says
   otherwise. If a dependency is not DONE, STOP and write why into §5. Say in one line which stage
   you are building, then continue without waiting.
3. STAGE DOC: read docs/stages/STAGE-NN-*.md. If it does not exist, write it first to
   EXECUTION_STANDARD.md Part 1 — every type, file path and test named, every default a number, an
   ordered checkboxed build list — then commit it. Read everything under its "Reference reading".
   Branch: git checkout -b stage-NN-<slug>.
4. BUILD: work the build list in order. For each part — code it, test it, dotnet build -c Release
   (zero warnings in Domain/Application), dotnet test (all green), tick the box IN the stage
   document, commit feat(stage-NN): <part>. Do not start the next part until this one is green. Do
   not add anything the document does not ask for. If document and code disagree, the document wins.
5. AGENTS: launch architecture-guard, then the specialists for what this stage touched (see
   docs/AGENTS.md), then stage-verifier until PASS or documented UNVERIFIED. Fix every finding, or
   write the reason into PROGRESS.md. UNVERIFIED is not PASS. "It builds" is not PASS.
6. VERIFY BY EXECUTING: state the warning count, the test count, the measured coverage figure; RUN
   the migration Down then Up; RUN the seed and quote the rows; confirm the endpoints are in
   openapi/v1.json; confirm a permission test per high-risk operation. Anything you could not run
   here is "UNVERIFIED — needs <machine>", never ticked.
7. DOCUMENT: PROGRESS.md §1 row, a session-log entry carrying the evidence above, §4 for defects
   found, §5 rewritten for the next session. ADRs in DECISIONS.md. ROADMAP.md row. DATA_MODEL.md /
   SYNC_AND_BACKUP.md / TESTING.md where changed. Tick the exit checklist only where executed.
8. LAND: commit feat(stage-NN): <stage title>, merge to main if the exit checklist passes, push,
   then update the parent ecosystem superproject's vuma pointer and push that. Report what shipped,
   the test count, the coverage, what is UNVERIFIED and where.

GUARDRAILS — check before step 5: company_id on every business table and no handler touching two
databases; no cross-company operation without RequireLink; Available not OnHand answers "can I sell
this", and group projections plan while the owning company's database commits; tax per company per
document, rounded once; no module names a GL account; no module implements its own approval logic;
every replay path idempotent on a stable key proven by a constraint; a declared permission that
nothing checks is a defect; stock is never a mutable column; posted documents are immutable; a
language model classifies and phrases but never computes or reads data; nothing hard-coded that is
configuration.
```

## Aiming it at something specific

The command takes the next stage by default. To point it somewhere else, say so after it:

```
/next-stage 14b — the rep module
/next-stage fix the four Stage 09 defects in PROGRESS.md §4.11–§4.15 first, then carry on
/next-stage run the agent panel against Stages 10, 11, 12 and 13 (§4.17) before starting anything new
```

## Overnight, unattended

`scripts/run-autonomous.ps1` runs the same protocol one stage per invocation, each with a fresh
context window. Read `docs/AUTONOMOUS_OPERATION.md` once first — there is one-time setup without which
background runs are refused.

## What "done" means when the session says it is done

A stage is DONE when its own exit checklist and `CLAUDE.md` §8 both pass, `stage-verifier` returned
PASS, and the push landed. `UNVERIFIED` is not `PASS`. "It builds" is not `PASS`. "Merged" is not
`PASS` — Stage 09 was merged and is still reopened.
