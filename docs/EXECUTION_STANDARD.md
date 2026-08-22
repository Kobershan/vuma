# EXECUTION STANDARD — how a stage document is written, and how a session executes one

**Read this before writing a stage document and before executing one.** It exists because the sessions
that build this product are not always the model that planned it. A stage document that requires
judgement to interpret will get inconsistent judgement. A stage document that reads like an instruction
manual gets built the same way every time (ADR-133).

---

## Part 1 — Writing a stage document

Every stage document is written **before** its code and lives at `docs/stages/STAGE-NN-<slug>.md`. It
must be executable by a session that has read nothing but `CLAUDE.md`, `docs/PROGRESS.md`,
`docs/DECISIONS.md` and the documents it names under "Reference reading".

### The eight required sections, in this order

1. **Header line** — `**Status:** … · **Depends on:** … · **Reference reading:** …`
   List *every* document and ADR the session must read, with section numbers. "Read the docs" is not a
   reference list.
2. **Objective** — three paragraphs at most. What exists at the end that did not exist at the start.
3. **What this stage does not own** — the boundaries, each with the stage that does own it. This section
   prevents more rework than any other, because scope creep is what a session invents when a boundary is
   unstated.
4. **Deliverables** — grouped by layer (Domain / Application / Infrastructure / API / Permissions /
   Entitlement). **Name every type.** `SalesOrder`, `IPickAllocationStrategy`,
   `ConfirmPutawayCommandHandler` — not "an order aggregate" or "a strategy for allocation".
5. **Business rules** — numbered, each one testable. A rule that cannot be failed by a concrete input is
   a description, not a rule; delete it or sharpen it.
6. **Parts — the build list** — ordered, checkboxed, each part small enough to compile and commit on its
   own. This is the working checklist, ticked as the session goes. See Part 2 below.
7. **Tests / acceptance** — concrete scenarios with concrete numbers and the expected outcome. "Test the
   allocation logic" is not acceptance. "20 demanded, 12 in company A and 3 in company B → 15 allocated,
   5 backordered, nothing negative" is.
8. **Exit checklist** — every box independently verifiable by executing something, plus `CLAUDE.md` §8.

### Rules for the prose

- **Name the file paths.** `src/VumaRetail.Domain/Orders/SalesOrder.cs`, not "the domain project".
- **Name the tests.** `Two_concurrent_holds_against_one_limit_admit_exactly_one` beats "a concurrency
  test".
- **Give every number.** Defaults, timeouts, expiries, tolerances, retry counts, page sizes. A session
  asked to pick a timeout will pick a different one each time.
- **State the decision, not the options.** If two designs were considered, the ADR records the loser.
  The stage document carries only the winner.
- **Leave no open question.** If the document says "decide whether…", it is not finished. Decide it,
  write it as an ADR, and reference the ADR.
- **Say what must NOT be done**, where it is tempting: "do not fork the pick model", "do not add a second
  WhatsApp client", "do not compute tax on the basket".
- **Mark what cannot be verified on this machine** (`PROGRESS.md` §4.3 — Windows, WPF, FlaUI, Docker) and
  say which machine must verify it. `UNVERIFIED` is never `PASS`.

### The stage-document template

`docs/stages/STAGE-04b-licensing.md` and `docs/stages/STAGE-14-order-management.md` are the two worked
examples. Copy the structure of 14 for a module stage; it has the best build list in the repository.

---

## Part 2 — Executing a stage

### Before writing code

1. Read everything in the header's reference list. All of it. Stage documents assume it.
2. Confirm the stage's dependencies are DONE in `PROGRESS.md` §1. If one is not, stop and say so in
   `PROGRESS.md` §5 rather than building on sand.
3. Create the branch: `git checkout -b stage-NN-<slug>` off `main`.
4. Tick part A of the build list as you complete each item, **in the document, as you go**. A build list
   ticked at the end is a summary, not a checklist.

### While building

- **Commit at every green checkpoint**, not once at the end. `feat(stage-NN): <part>` for parts,
  `feat(stage-NN): <stage title>` for the stage commit.
- **Run `dotnet build -c Release` and `dotnet test` after every part.** Finding a break three parts later
  costs more than the check.
- **Write the test with the code, not after the stage.** The stage's coverage bar is 80% on its new
  Domain + Application, and a suite written afterwards tests what was built rather than what was
  specified.
- **When the document and the code disagree, the document wins** — unless the document is wrong, in
  which case fix the document in the same commit and note it in `PROGRESS.md`. Silent divergence is the
  failure mode this rule exists to stop.
- **Never ask the user anything** (`CLAUDE.md` §1). Decide, write an ADR, continue.

### Verifying — execute, do not assert

The exit checklist is worked by **running things**, and the evidence goes in `PROGRESS.md`:

| Claim | What proves it |
|---|---|
| "Build is clean" | The `dotnet build -c Release` output, warning count stated |
| "Tests pass" | The count. `1,157 tests green, 0 skipped` |
| "Coverage ≥ 80%" | The measured figure for this stage's Domain + Application |
| "Migration is reversible" | `Down` **executed**, not read |
| "It is demonstrable" | The seed ran and the resulting rows are quoted |
| "Permissions enforced" | A test per high-risk operation, not one standing in for the group |
| "Endpoints documented" | They appear in `/openapi/v1.json` |

A box that could not be executed on this machine is marked `UNVERIFIED — needs <machine>`, never ticked.

### Finishing

1. Run the agent panel (`docs/AGENTS.md`) and close every finding, or record the reason in
   `PROGRESS.md`.
2. Update `PROGRESS.md` — §1 row, session log entry with the evidence above, §4 for anything found, §5
   rewritten so the next session knows exactly where to start.
3. Append ADRs for every decision taken.
4. Merge to `main` when the exit checklist passes, push, then update the parent `ecosystem`
   superproject's `vuma` pointer and push that.

### When it will not fit in one session

Stop cleanly at a green build. Write into `PROGRESS.md` §5: what is done (with ticked build-list parts),
what is half-done and exactly where, the next concrete action, and anything learned that is not obvious
from the code. Commit, push. A truthful handoff is worth more than an extra hour of degraded work.

---

## Part 3 — The ten mistakes this repository has actually made

Written from `PROGRESS.md` §4. Read them once; they are cheaper to avoid than to find.

1. **Marking a stage DONE on a self-review.** Stage 09 was DONE for six hours; the agents found eight
   defects, four serious. Run the panel.
2. **A green suite that asserts nothing.** Stage 13 arrived passing 1,147 tests with four acceptance
   gaps open. Coverage is not acceptance.
3. **A replay path that is not idempotent** (§4.11). Anything that can be delivered twice will be.
4. **Reading on-hand where available belongs.** Two numbers, and the wrong one oversells.
5. **A permission declared and never checked** (§4.15). Declaring is not enforcing.
6. **Rounding twice.** Round once, at the extended price, and assert the 0.005 boundary (§4.14).
7. **A gate with no call sites** (§4.12). `RequireModule` existed for four stages and gated nothing.
8. **A sweep that swept nothing** (§4.18). Load-order dependence made two architecture tests vacuous.
9. **Hard-coding what is configuration** — tax rates, locale, currency, timeouts. Tax is a rules engine.
10. **Fixing the document instead of the code, or the code instead of the document.** Both, together, in
    one commit.
