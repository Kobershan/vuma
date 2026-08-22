# AGENTS — Vuma Retail

Subagent definitions live in `.claude/agents/`. Each one is a specialist reviewer with a narrow
brief and its own context window. They exist so that the session building a stage is not also the
session judging it — the author is the least reliable judge of their own work.

## The roster

| Agent | Use it when | Writes code? |
|---|---|---|
| `architecture-guard` | A stage adds a project reference, a cross-module call, or a command handler. Before any stage is marked DONE. | No — reports only |
| `stage-verifier` | End of every stage, before updating `PROGRESS.md` and committing. Runs the Exit Checklist and §8 Definition of Done for real. | No — reports only |
| `licence-safety` | Anything touching licensing, entitlements, the enforcement ladder, heartbeats, leases, metering or the control plane. Every stage from 04b onward that adds a command. | No — reports only |
| `money-and-tax` | Anything touching pricing, discounts, promotions, tax, cost, valuation, currency or GL posting. | No — reports only |
| `sync-and-offline` | A stage adds a replicated entity or touches the outbox/inbox, HLC clock, conflict resolution, terminal SQLite cache, or a POS path that must survive network loss. | No — reports only |
| `multi-company-guard` | Company scoping, group scope, credit groups, cross-company receipting or payment, inter-company clearing, document splitting, group barcode resolution, consolidated reporting. Every stage from 06c onward that adds a business table. | No — reports only |
| `field-sales-guard` | Reps, territories, pro forma orders and credit notes, the approval-to-invoice path, rep stock visibility, rep performance reporting. | No — reports only |
| `stock-availability-guard` | Reservations, available-to-promise, cross-company sourcing, pick waves, staging areas, stock location state, cycle and interval counts. | No — reports only |
| `conversation-safety` | The WhatsApp/email assistant: inbound messages, intent classification, reply composition, contact bindings and OTP, document delivery links, bot-placed orders. | No — reports only |
| `doc-scribe` | End of a stage, or when context runs low and the session must stop cleanly. | Docs only, never source |

## How the main session uses them

The main session executes the stage. It calls agents at the points above and **acts on their
findings before committing**. An agent's report is advice, not a merge gate — but ignoring a
finding requires a reason recorded in `PROGRESS.md`, not silence.

Recommended end-of-stage sequence:

```
1. Build the stage to completion
2. architecture-guard          → fix violations
3. the domain specialists that apply:
      money-and-tax             pricing, tax, cost, valuation, currency, GL posting
      sync-and-offline          replicated entities, outbox/inbox, offline paths
      licence-safety            entitlements, leases, metering, read-only
      multi-company-guard       company scoping, group credit, cross-company money, split documents
      field-sales-guard         reps, pro formas, approval-to-invoice, rep performance
      stock-availability-guard  reservations, availability, staging states, waves, counts
      conversation-safety       the assistant: invented figures, identity, injection, consent
4. stage-verifier              → fix failures, re-run until PASS or documented UNVERIFIED
5. doc-scribe                  → PROGRESS.md handoff + ADRs
6. commit:  feat(stage-NN): <stage title>   then push
```

`/next-stage` (`.claude/commands/next-stage.md`) runs this sequence as written. A stage that skips it
is not DONE — see `PROGRESS.md` §4.17, which is the standing record of what skipping it has cost.

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

## Which specialists a stage actually needs

Running all seven specialists on every stage wastes context and dulls the reports. Run the ones whose
subject the stage touched — and when in doubt, run it. The mapping for the stages currently planned:

| Stage | Specialists |
|---|---|
| 06c multi-company foundation | `multi-company-guard`, `architecture-guard`, `sync-and-offline` |
| 06d group services | `multi-company-guard`, `money-and-tax` (credit exposure) |
| 06e trading group | `multi-company-guard`, `licence-safety`, `architecture-guard` |
| 07c cross-company money | `multi-company-guard`, `money-and-tax` |
| 08c availability & sourcing | `stock-availability-guard`, `multi-company-guard` |
| 10c quotes & invoices | `money-and-tax`, `multi-company-guard` |
| 13b picking waves & staging | `stock-availability-guard`, `sync-and-offline` |
| 14 order management | `stock-availability-guard`, `money-and-tax`, `sync-and-offline` |
| 09b mixed basket | `money-and-tax` (per-segment tax and rounding), `multi-company-guard`, `stock-availability-guard`, `sync-and-offline` |
| 14b field sales | `field-sales-guard`, `stock-availability-guard`, `multi-company-guard`, `money-and-tax`, `sync-and-offline` |
| 22b conversational commerce | `conversation-safety`, `multi-company-guard`, `licence-safety` |

## Adding an agent

Create `.claude/agents/<name>.md` with `name`, `description` and `tools` frontmatter, add a row to
the table above, and note it in the stage's ADR if the agent encodes a new rule. Keep the brief
narrow — a general-purpose reviewer finds less than a specialist with one job.

**The build session may not write to `.claude/**`** (`CLAUDE.md` §1) — that path belongs to the human
operator, and writing there stalls an unattended run. A session that concludes a new agent is needed
says so in `PROGRESS.md` and writes the brief there; the operator creates the file.
