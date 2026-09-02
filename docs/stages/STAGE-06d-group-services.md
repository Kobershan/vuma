# STAGE 06d — Group services: sagas, credit groups, the routing index and the group read models

**Status:** NOT_STARTED · **Depends on:** 06c · **Reference reading:** `docs/MULTI_COMPANY.md` §3, §4, §6, §11, `docs/DECISIONS.md` ADR-100, ADR-101, ADR-116, ADR-119

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 06d-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-06D-001 | Implement saga coordination | Stage 06c | IN_PROGRESS |
| TASK-06D-002 | Implement group credit holds | TASK-06D-001 | NOT_STARTED |
| TASK-06D-003 | Implement barcode routing and group read models | TASK-06D-001 | NOT_STARTED |
| TASK-06D-004 | Complete Stage 06d verification and seed | TASK-06D-001 through TASK-06D-003 | NOT_STARTED |

## Objective

Everything that spans companies, built once: the saga coordinator that 07c, 08c and 14b all dispatch
through, the credit group and its hold tokens, the barcode routing index, and the group read models that
give the operator one view over many databases.

Split from 06c by the roadmap's own rule — 06c is plumbing, this is the first stage where a user can see
something.

## Deliverables

**Saga coordination** (ADR-116)
- `ISagaCoordinator` — write an immutable intent recording the whole operation *before* anything happens;
  dispatch each leg to exactly one company database, idempotent on `(intent_id, leg_id)`; track
  acknowledgement; retry with backoff indefinitely; compensate in reverse on failure; alarm on timeout.
- **Compensation is a new document** — a release, a reversal, a credit note — never a delete or an edit
  (`CLAUDE.md` §7 rule 7).
- `/api/v1/admin/intents` — the in-flight report: outstanding intents, unapplied legs, ages, owners.

**Credit groups** (ADR-101)
- `registry.credit_groups` — direction (`Receivable` | `Payable`), limit + currency, exposure policy.
- `registry.credit_group_members` — company, partner, optional sub-limit.
- `registry.credit_holds` — amount, company, document reference, expiry, state; append-only.
- `registry.credit_exposure_entries` — append-only confirmed consumption, idempotent per document.
- `IGroupCreditService` — `GetPosition`, `TryHold` (serialisable, in the registry alone), `Confirm`,
  `Release`. Exposure = confirmed + unexpired holds.
- A background job expiring holds, and a held-but-unconfirmed report.

**Catalogue routing** (ADR-100)
- `registry.catalog_routing_index`, published by each company's outbox on barcode create/change/retire,
  rebuildable by asking every company to republish.
- `IBarcodeResolver` — one probe, then the item detail from the owning company. Collisions return
  `MultipleCompanyMatches`; the resolver never picks one.
- Degradation: registry unreachable → resolve against the scanning company only, and say so.

**Group read models** (ADR-119)
- `registry.group_availability`, `registry.group_partner_exposure`, `registry.company_period_figures` —
  each fed by company outboxes, each carrying `AsAt` per contributing company.
- `IGroupReadStore`, and a rebuild path tested to equal the incremental projection.
- **No group read model may be the basis for a commit**, and there is a review rule and a test fixture
  that feeds a deliberately stale projection to prove callers re-check.

**API** — `/api/v1/scan/{barcode}`, `/api/v1/credit-groups` (position, hold, confirm, release),
`/api/v1/group/availability`, `/api/v1/admin/intents`. Every group response carries `AsAt` and names any
stale contributor.

**Permissions** — `group.view`, `group.credit.view`, `group.credit.manage`, `group.report`,
`platform.intents.view`.

## Business rules

1. A hold is taken in the registry in a serialisable transaction, and it expires by itself.
2. Exposure = confirmed consumption + unexpired holds. Never one without the other.
3. A member sub-limit narrows; it can never widen the group ceiling.
4. Registry unreachable → credit sales refused, cash sales continue.
5. A scan never guesses a company.
6. Every group figure crossing an API boundary carries `AsAt`; a stale contributor is disclosed, never
   silently summed.
7. Every saga leg is idempotent and retryable forever; nothing silently gives up.
8. An intent past its timeout is an alarm with a named owner, not a log line.

## Tests / acceptance

- Two concurrent `TryHold` calls against a group with R5 000 available and two R4 000 demands: exactly
  one succeeds. Real registry database, real serialisable transactions, genuinely parallel.
- Hold → crash before the document is written → the hold expires and the credit returns; exposure over
  the whole sequence is never wrong in the customer's favour.
- Hold → document → confirm, replayed twice: consumption counts once.
- A barcode in two companies returns both candidates; in one company it resolves in one probe plus one
  company read, asserted with a query counter.
- Registry stopped: a scan still resolves locally and is labelled local; a credit sale is refused; a cash
  sale completes.
- A saga leg that fails three times then succeeds applies exactly once.
- A rebuilt group availability projection equals the incremental one after 500 randomised movements
  across three company databases.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `multi-company-guard`, `architecture-guard`, `money-and-tax` (credit exposure) run, findings closed
- [ ] Migration reversible on the registry chain, `Down` executed
- [ ] `docs/DATA_MODEL.md` §4l (registry) filled in; replication registry updated
- [ ] Seed: three companies, one shared customer with a R150 000 group limit spread across all three,
      one barcode collision, one deliberately stale projection fixture
