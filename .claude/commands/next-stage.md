---
description: Execute the next incomplete Vuma Retail stage end-to-end — build, agent reviews, docs, commit, push
---

Execute the next incomplete stage of Vuma Retail to completion in this one session. Do not ask me
anything — decide, record the decision as an ADR, and carry on (`CLAUDE.md` §1).

## 1. Load the contract

Read, in this order, before touching code:

1. `CLAUDE.md` — the contract, in full
1b. `docs/EXECUTION_STANDARD.md` — how a stage document is written and how you execute one. Part 2 is
    your working method; Part 3 is the ten mistakes this repository has already made
2. `docs/PROGRESS.md` §1 (stage status) and §5 (next session starts here) — **§5 names the stage; it
   wins over any other source**
3. `docs/ROADMAP.md` — dependencies and the stage's place in the plan
4. `docs/DECISIONS.md` — LOCKED decisions. Never re-litigate one; supersede it with a new ADR or
   conform to it
5. `docs/stages/STAGE-NN-*.md` for the stage you are about to build. **If it does not exist, write it
   first, to `docs/EXECUTION_STANDARD.md` Part 1** — eight sections, every type and file path named,
   every default given as a number, an ordered checkboxed build list, concrete acceptance scenarios —
   then build it. `STAGE-06e-trading-group.md` and `STAGE-14-order-management.md` are the worked
   examples
6. Whatever that stage lists under "Reference reading"

## 2. Pick the stage

Take the first stage that is NOT_STARTED or IN_PROGRESS in `docs/PROGRESS.md` §1 **and** whose
dependencies are DONE, unless §5 names a different one — §5 is the truth. Skip a stage only if it is
blocked on this machine (`PROGRESS.md` §4.3: Windows, WPF, FlaUI, Docker) and say so in the handoff.
Announce the chosen stage in one line before starting.

## 3. Build it, whole

Every deliverable in the stage document: domain, application, infrastructure, API, migration, tests,
seed data, permissions, sync registration, posting rules, approval policies, entitlement flag,
metering counters. `CLAUDE.md` §8 is the bar. Commit at green checkpoints as you go — never leave the
repo non-compiling.

**Tick the build list in the stage document as you go**, not at the end. Run
`dotnet build -c Release` and `dotnet test` after every part. Where the document and the code disagree,
the document wins — unless the document is wrong, in which case fix it in the same commit and say so in
`PROGRESS.md`.

**Verify by executing, never by asserting.** "Migration is reversible" means `Down` ran. "Tests pass"
means a count. "It is demonstrable" means the seed ran and you can quote the rows. A box you could not
execute on this machine is `UNVERIFIED — needs <machine>`, never ticked
(`docs/EXECUTION_STANDARD.md` Part 2).

## 4. Run the agent panel — this is not optional

The author is the least reliable judge of their own work. Launch these as subagents (`docs/AGENTS.md`),
and **act on every finding before committing**; ignoring one requires a written reason in
`docs/PROGRESS.md`, not silence:

1. `architecture-guard` — always
2. the specialists that apply to what the stage touched:
   - `money-and-tax` — pricing, tax, cost, valuation, currency, GL posting
   - `sync-and-offline` — replicated entities, outbox/inbox, offline paths
   - `licence-safety` — entitlements, leases, metering, read-only
   - `multi-company-guard` — company scoping, group credit, cross-company money, split documents
   - `field-sales-guard` — reps, pro formas, approval-to-invoice, rep performance
   - `stock-availability-guard` — reservations, available-to-promise, staging states, waves, counts
   - `conversation-safety` — the assistant: invented figures, identity, injection, consent
3. `stage-verifier` — runs the Exit Checklist and §8 for real. Re-run until PASS or a documented
   UNVERIFIED. **`UNVERIFIED` is not `PASS`, and neither is "merged".**
4. `doc-scribe` — the handoff

## 5. Update the markdown

- `docs/PROGRESS.md` — §1 status row, a session-log entry (what shipped, test count, coverage,
  migration reversibility, what was verified by executing rather than asserting), §4 for any defect
  found, and §5 rewritten so the next session knows exactly where to start
- `docs/DECISIONS.md` — an ADR for every decision taken, numbered on from the last
- `docs/ROADMAP.md` — status against the stage row if it changed
- the stage document's own exit checklist, ticked only where it was actually executed
- `docs/DATA_MODEL.md`, `docs/SYNC_AND_BACKUP.md`, `docs/TESTING.md` where the stage changed them

## 6. Commit and push

```
git add -A
git commit -m "feat(stage-NN): <stage title>"
git push origin <branch>
```

If the work ran on a stage branch, merge it to `main` when the exit checklist passes, then push
`main`. Then update the parent `ecosystem` superproject's `vuma` pointer and push that too.

## 7. If you run out of room

Stop cleanly at a green build, write an honest handoff into `docs/PROGRESS.md` §5 — what is done, what
is half-done, the exact next action — commit, push. A truthful "not finished" beats a stage marked
DONE that isn't.
