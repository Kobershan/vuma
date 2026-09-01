# STAGE 08c — Cross-company availability, reservations and split fulfilment

**Status:** NOT_STARTED · **Depends on:** 08, 06c, 06d · **Reference reading:** `docs/MULTI_COMPANY.md` §4, §5, `docs/DECISIONS.md` ADR-102, ADR-103, ADR-108, ADR-116, ADR-119, ADR-005, `CLAUDE.md` §7 rule 6

## Task index
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) for project and boundary rules, then load only the references named by the eventual task.

**Architecture checklist:** WHAT/WHY come from this stage's Objective; affected layers/components come from its Deliverables; data, API, security, multi-company, synchronization, licensing, and testing rules come from the linked authority documents. Missing answers are **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs in the header apply; a new ADR is required only for a new decision. Nothing outside stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 08c-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This is a planning gate, not an implementation task. Before this stage is selected, replace it with independently executable task files using the canonical template in docs/tasks/README.md.

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-08C-001 | Implement availability and reservation ledger | Stages 06c, 06d, 06e, 08 | NOT_STARTED |
| TASK-08C-002 | Implement sourcing and split fulfilment | TASK-08C-001 | NOT_STARTED |
| TASK-08C-003 | Complete Stage 08c verification | TASK-08C-001, TASK-08C-002 | NOT_STARTED |

## Objective

Answer two questions the rest of the product keeps asking: **how much can I actually sell right now**,
and **which company's stock fills this line**. The second question now crosses databases, and the answer
to it is planned from a projection that is stale by construction — so this stage's real job is the
two-step split in ADR-102: **plan from the group view, commit in the owning company's database.**

## Deliverables

**Domain**
- `StockReservation` — append-only, immutable: company, location, item/variant, quantity, source
  document (order, pro forma approval, transfer), state (`Held`, `Consumed`, `Released`, `Expired`),
  expiry, and the reason. A release is a new entry, never an edit (ADR-005's shape, ADR-103).
- `AvailableToPromise` value object — `OnHand`, `Reserved`, `InStaging`, `Incoming`, `Available`, and
  the `AsAt` timestamp that every consumer must display.
- `SourcingPlan` / `SourcingAllocation` — the decision: which company and location supplies how much
  of each line, and what remains as backorder.

**Application**
- `IAvailabilityService` — **company-local** reads (authoritative, `Available = OnHand − Reserved −
  InStaging`) and **group** reads served from 06d's registry projection (planning only, always stamped
  `AsAt`). The two return different types so a caller cannot use one where the other belongs — this is
  the type system doing the work a review otherwise has to.
- `IReservationService` — `Reserve`, `Consume`, `Release`, `Expire`, **always inside one company's own
  database**, one serialisable transaction with the availability re-check. The check-then-act race is
  the defect this stage exists to prevent, and a stale group projection must never be able to cause it.
- `ICompanySourcingStrategy` — default `AvailabilityThenProximity`; ordering company first, then
  nearest store, then most available. Behind an interface for Stage 15 to replace.
- `ISplitDocumentBuilder` — turns a committed sourcing plan into one document per supplying company,
  each written entirely inside its own database, sharing a `GroupDocumentRef`, and asserts the split
  reconciles to the source line for line and cent for cent.
- **Commit is a saga** (06d's `ISagaCoordinator`): one reservation leg per company, compensated by a
  release if a later leg fails. There is no transaction spanning two companies (ADR-116).
- A background job expiring stale reservations, configurable per tenant, defaulting to 72 hours for
  order reservations and none for shipped-pending.

**Infrastructure**
- `inventory.stock_reservations` (append-only) and `inventory.available_balances` (projection, rebuilt
  from ledger + reservations, with a rebuild test that must equal the incremental projection).
- Each company database publishes availability changes to the registry projection through its existing
  outbox. The group query is one read of that projection, not N queries — and it is labelled stale when
  a contributor has not published recently (ADR-119).

**API** — `/api/v1/availability?groupScope=true`, `/api/v1/sourcing/plan` (a dry run any caller can ask
for before committing), reservation endpoints for internal callers.

**Permissions** — `inventory.availability.view`, `inventory.reservation.manage`, `group.view` for the
group-scoped read.

## Business rules

1. **Available never goes negative, in any company, under any interleaving — including when the planner
   is fed a stale projection.** This is the stage's one non-negotiable rule. Stale group data may cause
   a re-plan; it may never cause an oversell, because nothing commits a company's stock except a
   serialisable transaction inside that company's own database.
2. A reservation reduces *available* and leaves *on hand* alone. On-hand changes only when stock
   physically moves, through the existing stock ledger.
3. A line that group-wide availability cannot cover allocates what exists and backorders the rest. It
   never allocates against a company that has none.
4. A sourcing plan is a projection until committed. Committing takes one local reservation per company
   as saga legs; a failed leg compensates the ones already taken, by release rows, never by deletion.
   A short leg re-sources once, then backorders.
5. Reservations expire, and expiry writes a ledger entry so the history explains itself.
6. Every availability figure crossing an API boundary carries `AsAt`.
7. A split document set reconciles exactly to its source: every line on exactly one document, sums
   equal, no rounding drift.

## Tests / acceptance

- 20 units demanded, 12 in company A and 30 in company B: 12 + 8 allocated, no backorder, B not driven
  below zero.
- 20 demanded, 12 in A and 3 in B: 15 allocated, 5 backordered, nothing negative.
- Two concurrent orders for the last 5 units in one company: one gets 5, the other backorders 5. Real
  database, genuinely parallel transactions.
- **The stale-projection test**: the planner is handed a projection saying company B has 30 when it has
  3. Outcome is 3 reserved and the rest backordered — never a negative, never an oversell.
- A two-company commit where the second leg fails: the first company's reservation is released by a new
  ledger row, the order is not created, and availability in both companies is exactly where it started.
- Reserve → release → reserve leaves availability exactly where it started, with three ledger entries.
- Availability projection rebuilt from scratch equals the incremental projection after 500 random
  movements and reservations.
- A committed sourcing plan across two companies produces two documents whose lines sum to the order.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `stock-availability-guard`, `multi-company-guard`, `architecture-guard` run, findings closed
- [ ] Migration reversible, `Down` executed
- [ ] `docs/DATA_MODEL.md` §4m extended; replication registry updated for `StockReservation`
- [ ] The group availability projection publishes from every company and rebuilds equal to incremental
- [ ] Seed: an order sourced across two companies, reserved, visible in availability
