# AGENTS — Vuma Retail

Subagent definitions live in `.claude/agents/`. Each one is a specialist reviewer with a narrow
brief and its own context window. They exist so that the session building a stage is not also the
session judging it — the author is the least reliable judge of their own work.

## The roster

| Agent | Use it when | Writes code? |
|---|---|---|
| `stage-verifier` | End of every stage, before updating `PROGRESS.md` and committing. Runs the layering/architecture rules (§7, formerly the separate `architecture-guard`), the Exit Checklist and §8 Definition of Done, all in one pass. | No — reports only |
| `licence-safety` | Anything touching licensing, entitlements, the enforcement ladder, heartbeats, leases, metering or the control plane. Every stage from 04b onward that adds a command. | No — reports only |
| `money-and-tax` | Anything touching pricing, discounts, promotions, tax, cost, valuation, currency or GL posting. | No — reports only |
| `sync-and-offline` | A stage adds a replicated entity or touches the outbox/inbox, HLC clock, conflict resolution, terminal SQLite cache, or a POS path that must survive network loss. | No — reports only |
| `doc-scribe` | End of a stage, or when context runs low and the session must stop cleanly. | Docs only, never source |

## How the main session uses them

The main session executes the stage. It calls agents at the points above and **acts on their
findings before committing**. An agent's report is advice, not a merge gate — but ignoring a
finding requires a reason recorded in `PROGRESS.md`, not silence.

Recommended end-of-stage sequence — kept deliberately short. Every unconditional agent pass is a full
extra context load; only `stage-verifier` and `doc-scribe` run on every stage. The domain specialists
run only when they actually apply, not as a default sweep:

```
1. Build the stage to completion
2. the domain specialists that actually apply — usually 0-2 of money-and-tax / sync-and-offline /
   licence-safety, judged from what the stage touched, not run as a matter of course
3. stage-verifier              → architecture rules + Exit Checklist + Definition of Done, one pass;
                                  fix failures, re-run until PASS or documented UNVERIFIED
4. doc-scribe                  → PROGRESS.md handoff (short) + DECISIONS.md index entry (one line;
                                  full rationale goes to docs/archive/DECISIONS-ARCHIVE.md)
5. commit:  feat(stage-NN): <stage title>
```

That is 2-4 subagent passes per stage, not 6. `architecture-guard` was merged into `stage-verifier` —
both used to run unconditionally on every stage, so splitting them bought no extra coverage, only a
second full read of the same diff.

## Rules that apply to every agent

- **`UNVERIFIED` is not `PASS`.** This repository is developed on a Linux box while the product
  targets Windows. WPF and FlaUI cannot build or run here; Testcontainers needs Docker. An agent
  must say which checks it could not execute and which machine has to re-run them, rather than
  inferring a pass from code that looks correct.
- **A finding needs a worked example** — concrete inputs, the resulting wrong behaviour. An
  unverified suspicion is reported as a suspicion.
- **Agents do not re-litigate LOCKED ADRs.** If an agent believes a locked decision is wrong, it
  says so and recommends a superseding ADR; it does not flag conformant code as a defect.
- **Superseded ADRs are not live.** ADR-023 and ADR-027 are superseded by ADR-028 (lapsed
  subscription means read-only, never automatic lockout). Any finding that cites them as current is
  itself the error.
- Agents are read-only except `doc-scribe`, which writes documentation and never source.

## Adding an agent

Create `.claude/agents/<name>.md` with `name`, `description` and `tools` frontmatter, add a row to
the table above, and note it in the stage's ADR if the agent encodes a new rule. Keep the brief
narrow — a general-purpose reviewer finds less than a specialist with one job.
