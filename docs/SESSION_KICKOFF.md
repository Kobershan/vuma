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
Read CLAUDE.md, docs/PROGRESS.md (§1 and §5), docs/ROADMAP.md and docs/DECISIONS.md, then execute
the next incomplete stage to completion in this session, following the session protocol in
CLAUDE.md §0. Write the stage document first if docs/stages/ does not have one. Run the agent panel
from docs/AGENTS.md before committing — architecture-guard, the specialists that apply,
stage-verifier until PASS or a documented UNVERIFIED, then doc-scribe. Update PROGRESS.md,
DECISIONS.md and ROADMAP.md, commit as feat(stage-NN): <title>, and push. Do not ask me any
questions.
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
