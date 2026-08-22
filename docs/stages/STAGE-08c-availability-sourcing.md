# STAGE 08c — Cross-company availability, reservations and split fulfilment

**Status:** NOT_STARTED · **Depends on:** 08, 06c · **Reference reading:** `docs/MULTI_COMPANY.md` §4, §5, `docs/DECISIONS.md` ADR-102, ADR-103, ADR-108, ADR-005, `CLAUDE.md` §7 rule 6

## Objective

Answer two questions the rest of the product keeps asking: **how much can I actually sell right now**,
and **which company's stock fills this line**. Build the reservation ledger that makes the first answer
safe under concurrency, and the sourcing engine that makes the second answer never produce a negative.

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
- `IAvailabilityService` — company-scoped and group-scoped reads. `Available = OnHand − Reserved −
  InStaging`. Never returns on-hand as if it were available.
- `IReservationService` — `Reserve`, `Consume`, `Release`, `Expire`. Reserving is one transaction with
  the availability check; the check-then-act race is the defect this stage exists to prevent.
- `ICompanySourcingStrategy` — default `AvailabilityThenProximity`; ordering company first, then
  nearest store, then most available. Behind an interface for Stage 15 to replace.
- `ISplitDocumentBuilder` — turns a sourcing plan into one document per supplying company, with a
  shared `GroupDocumentRef`, and asserts the split reconciles to the source line for line.
- A background job expiring stale reservations, configurable per tenant, defaulting to 72 hours for
  order reservations and none for shipped-pending.

**Infrastructure**
- `inventory.stock_reservations` (append-only) and `inventory.available_balances` (projection, rebuilt
  from ledger + reservations, with a rebuild test that must equal the incremental projection).
- Group availability query: one indexed query across companies, not N queries.

**API** — `/api/v1/availability?groupScope=true`, `/api/v1/sourcing/plan` (a dry run any caller can ask
for before committing), reservation endpoints for internal callers.

**Permissions** — `inventory.availability.view`, `inventory.reservation.manage`, `group.view` for the
group-scoped read.

## Business rules

1. **Available never goes negative, in any company, under any interleaving.** This is the stage's one
   non-negotiable rule and it is tested with real concurrent transactions.
2. A reservation reduces *available* and leaves *on hand* alone. On-hand changes only when stock
   physically moves, through the existing stock ledger.
3. A line that group-wide availability cannot cover allocates what exists and backorders the rest. It
   never allocates against a company that has none.
4. A sourcing plan is a projection until it is committed; committing it takes the reservations in one
   transaction or takes none of them.
5. Reservations expire, and expiry writes a ledger entry so the history explains itself.
6. Every availability figure crossing an API boundary carries `AsAt`.
7. A split document set reconciles exactly to its source: every line on exactly one document, sums
   equal, no rounding drift.

## Tests / acceptance

- 20 units demanded, 12 in company A and 30 in company B: 12 + 8 allocated, no backorder, B not driven
  below zero.
- 20 demanded, 12 in A and 3 in B: 15 allocated, 5 backordered, nothing negative.
- Two concurrent orders for the last 5 units: one gets 5, the other backorders 5. Real database.
- Reserve → release → reserve leaves availability exactly where it started, with three ledger entries.
- Availability projection rebuilt from scratch equals the incremental projection after 500 random
  movements and reservations.
- A committed sourcing plan across two companies produces two documents whose lines sum to the order.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `stock-availability-guard`, `multi-company-guard`, `architecture-guard` run, findings closed
- [ ] Migration reversible, `Down` executed
- [ ] `docs/DATA_MODEL.md` §4e extended; replication registry updated for `StockReservation`
- [ ] Seed: an order sourced across two companies, reserved, visible in availability
