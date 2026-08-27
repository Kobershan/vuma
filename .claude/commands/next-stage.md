---
description: Build one focused Vuma Retail task — load the current handoff, implement only that task, validate it, update state, and stop cleanly
---

You are building **one task** of the current Vuma Retail stage in this session. A stage is completed
across multiple task sessions.

Work through the phases below **in order**. Do not skip a phase. Do not reorder them. Tick each
checkbox in your own reply as you complete it so you can see where you are.

**Three rules that override everything else:**

1. **Never ask the user a question.** Not about scope, naming, libraries, or "shall I continue". Decide,
   write the decision into `docs/DECISIONS.md` as an ADR, and carry on. There is nobody to answer.
2. **Never leave the repository broken.** Commit at every green build. If you must stop, stop at a green
   build with a written handoff.
3. **Verify by executing, never by asserting.** "Tests pass" means you ran them and have a count.
   "Migration is reversible" means you ran `Down`. If you could not run it on this machine, write
   `UNVERIFIED — needs <machine>`. Never tick a box you did not execute.

---

## PHASE 1 — Read the contract (do not write any code yet)

- [ ] 1.1 Read `CLAUDE.md` in full.
- [ ] 1.2 Read `docs/EXECUTION_STANDARD.md` in full. Part 2 is how you work. Part 3 is the ten mistakes
      this repository has already made — you are about to be in a position to make all of them.
- [ ] 1.3 Read `docs/CURRENT.md`; consult `docs/PROGRESS.md` only for relevant history or known issues.
- [ ] 1.4 Read `docs/ROADMAP.md` for the stage's dependencies.
- [ ] 1.5 Read `docs/DECISIONS.md`. Decisions marked LOCKED are settled — conform to them or write a
      superseding ADR. Never quietly ignore one.

## PHASE 2 — Choose the current task, then stop and say which

- [ ] 2.1 Read `docs/CURRENT.md` and select its `CURRENT TASK`. Do not choose a different task unless
      explicitly authorized.
- [ ] 2.2 Check the task and stage dependencies. **If one is not: stop.** Write the blocker into the
      task and `docs/CURRENT.md`; do not build on an unfinished dependency.
- [ ] 2.3 Check `PROGRESS.md` §4.3 — if this task needs Windows, WPF, FlaUI or Docker, note now which
      parts will be `UNVERIFIED` and say so up front.
- [ ] 2.4 **Say in one line which task you are building and why.** Then continue without waiting.

## PHASE 3 — The stage document

- [ ] 3.1 Open `docs/stages/STAGE-NN-<slug>.md`.
- [ ] 3.2 **If it does not exist, write it now, before any code**, to `docs/EXECUTION_STANDARD.md`
      Part 1: eight sections, every type and file path named, every default given as a number, an
      ordered checkboxed build list, acceptance written as concrete inputs and expected outputs, and an
      explicit "what this stage does not own". Copy the shape of
      `docs/stages/STAGE-06e-trading-group.md` or `docs/stages/STAGE-14-order-management.md`.
      Commit it: `docs(stage-NN): stage document`.
- [ ] 3.3 Read every document and ADR listed under the stage's "Reference reading". All of them. The
      stage document assumes you have.
- [ ] 3.4 Use the existing task/stage branch; do not create a new branch if the task is already in a
      controlled worktree.

## PHASE 4 — Build the task, and nothing else

Work only the current task's scope. If the task is too large, split it and update the handoff before
implementing. Do not silently expand it to cover nearby defects.

For the task:

- [ ] 4.1 Write the code for this task only.
- [ ] 4.2 Write its tests with it. Not after the task.
- [ ] 4.3 `dotnet build -c Release` — zero errors, zero warnings in `Domain` and `Application`.
- [ ] 4.4 `dotnet test` — all green.
- [ ] 4.5 Update the task's checklist/status in the task document.
- [ ] 4.6 Commit at a green checkpoint: `feat(stage-NN): <task description>`.

Do not move to another task in this session unless explicitly authorized.

**If the stage document and the code disagree, the document wins** — unless the document is wrong, in
which case fix the document in the same commit and note it in `PROGRESS.md`. Never let them diverge
silently.

**Do not** add anything the stage document does not ask for. Scope creep is what a session invents when
it is unsure. If you believe something is missing, write an ADR saying so and keep to the document.

## PHASE 5 — The agent panel (this is not optional)

You are the least reliable judge of your own work. Launch each of these as a subagent, read its report,
and **fix what it finds before you commit anything else**.

- [ ] 5.1 `architecture-guard` — always.
- [ ] 5.2 The specialists whose subject this task touched (see `docs/AGENTS.md` for the stage → agent
      table). When in doubt, run it:
      - `money-and-tax` — pricing, tax, cost, valuation, currency, GL posting
      - `sync-and-offline` — replicated entities, outbox/inbox, offline paths
      - `licence-safety` — entitlements, leases, metering, read-only
      - `multi-company-guard` — company routing, links, sagas, credit, split documents
      - `field-sales-guard` — reps, pro formas, approval-to-invoice
      - `stock-availability-guard` — reservations, availability, staging, waves, counts
      - `conversation-safety` — the assistant: invented figures, identity, injection, consent
- [ ] 5.3 Fix every finding. If you are not fixing one, write the reason into `PROGRESS.md`. Silence is
      not an option.
- [ ] 5.4 `stage-verifier` — it runs the exit checklist and `CLAUDE.md` §8 for real. Fix and re-run
      until it returns PASS or a documented UNVERIFIED. **`UNVERIFIED` is not `PASS`. "It builds" is not
      `PASS`. "Merged" is not `PASS`.**

## PHASE 6 — Verify by executing

Run the task's applicable validation by **executing it**, and record the evidence. Stage-only checks
(migration, seed, full coverage, and the complete agent panel) belong at stage completion unless this
task specifically changes them:

- [ ] 6.1 `dotnet build -c Release` — state the warning count.
- [ ] 6.2 `dotnet test` — state the number passed and skipped.
- [ ] 6.3 Coverage on this stage's new Domain + Application — state the measured figure. Bar is 80%.
- [ ] 6.4 Run the migration `Down` and then `Up` again. State that you ran it.
- [ ] 6.5 Run the seed. Quote actual rows it produced.
- [ ] 6.6 Confirm the new endpoints appear in `/openapi/v1.json`.
- [ ] 6.7 Confirm a permission test exists per high-risk operation — not one standing in for the group.
- [ ] 6.8 Anything you could not run here: `UNVERIFIED — needs <machine>`, and say what must be re-run.

## PHASE 7 — Write the documentation

- [ ] 7.1 Update the task status and work log with files, tests, findings, and follow-ups.
- [ ] 7.2 Update `docs/CURRENT.md` with the completed task, next task, blockers, and test status.
- [ ] 7.3 `docs/PROGRESS.md` session log — add an entry with the evidence from Phase 6 when this task
      materially changes project state.
- [ ] 7.4 `docs/PROGRESS.md` §4 — add any material defect you found, including in other stages' code.
- [ ] 7.5 `docs/DECISIONS.md` — an ADR for every decision you took, numbered on from the last one.
- [ ] 7.6 `docs/ROADMAP.md` — update the stage row if its status changed.
- [ ] 7.7 `docs/DATA_MODEL.md`, `docs/SYNC_AND_BACKUP.md`, `docs/TESTING.md` — update wherever this
      task changed them.
- [ ] 7.8 Tick task and stage checklists only for evidence actually executed.

## PHASE 8 — Land it

- [ ] 8.1 `git add` only task-scoped files and commit at a green checkpoint.
- [ ] 8.2 Merge, push, and run the stage exit process only when all stage tasks are complete and the
      stage checklist passes. Do not update any parent repository from this repository's session.
- [ ] 8.3 Report to the user: what changed, task-level test results, what is UNVERIFIED and
      on which machine, and what the next session should do.

---

## Guardrails — the rules most often broken in this repository

Check your code against these before Phase 5. Each one has been violated here before.

- **`company_id` on every business table.** One company, one database, **one transaction** — no handler
  ever touches two databases. Cross-company work is a saga with idempotent legs and compensation by a
  new document, never a delete (`CLAUDE.md` §7 rule 20).
- **No cross-company operation without `RequireLink`** — Operator ID match, `Active` link, the specific
  scope, checked at the point of use (rule 23).
- **`Available`, not `OnHand`, answers "can I sell this".** A reservation reduces available; on-hand
  changes only when goods move. Group projections **plan**; the owning company's database **commits**
  (rules 21, 20).
- **Tax per company, per document.** Never on a basket. Round once, per segment. Assert the 0.005
  boundary (rule 24).
- **No module names a GL account.** Raise a financial event; the posting-rules engine decides (rule 12).
- **No module implements its own approval logic.** Stage 05's `IApprovalService` (rule 13).
- **Anything that can be delivered twice will be.** Every replay path is idempotent on a stable key, and
  you prove it with a uniqueness constraint, not a retry loop (`PROGRESS.md` §4.11).
- **A declared permission that nothing checks is a defect** (§4.15).
- **Stock is never a mutable column.** It is the projection of an append-only ledger (rule 6).
- **Posted financial documents are immutable.** Amend with a reversal (rule 7).
- **A language model classifies and phrases. It never computes and never reads data** (rule 25).
- **Nothing is hard-coded that is configuration** — tax rates, currency, locale, timeouts, expiries.

## If you run out of room

Stop cleanly at a green build. Write into the task and `docs/CURRENT.md`: completed requirements,
changed files, exact test results, blockers, and the next concrete action. Commit the safe checkpoint.
A truthful "not finished" is worth more than a stage marked DONE that isn't —
Stage 09 was marked DONE on a self-review and the agents found eight defects in it, four serious.
