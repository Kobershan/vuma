# STAGE 14b — Field sales: the rep module

**Status:** NOT_STARTED · **Depends on:** 14, 10c, 08c, 05 · **Reference reading:** `docs/FIELD_SALES.md` in full, `docs/MULTI_COMPANY.md` §4, §6, `docs/DECISIONS.md` ADR-107, ADR-108, ADR-103, ADR-109, ADR-110, ADR-112, `CLAUDE.md` §7 rules 12, 13

## Task index

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-14B-001 | Implement field-sales proposals and approval | Stages 14, 10c, 08c, 05 | NOT_STARTED |
| TASK-14B-002 | Complete field-sales verification and performance | TASK-14B-001 | NOT_STARTED |

## Objective

Reps on the road capture pro forma orders and pro forma credit notes against live availability;
management approves them; approval — and only approval — creates the real document and commits the
stock. Then measure what each rep does, per month, comparably over time.

## Deliverables

**Domain**
- `Rep` — a user with a territory (customers and/or geography), companies they may sell for, a
  visibility profile (may they see cost? margin?), and targets per period.
- `ProFormaOrder` / `ProFormaOrderLine` — customer, company, lines with quantity, uom, **pack size**,
  quoted unit price, the price-list and promotion snapshot, availability as-at, expiry, status
  (`Draft | Submitted | Approved | Amended | Rejected | Expired | Converted`).
- `ProFormaCreditNote` / lines — references the original invoice and its lines, reason code, same
  status set.
- `RepTarget` — versioned; changing a target never rewrites a measured period.
- `RepPerformanceSnapshot` — closed-period, immutable, per rep per company, with the group roll-up.

**Application**
- Commands: capture, submit, amend, withdraw a pro forma; approve, reject, amend-and-return.
- **Approval path**: `IApprovalService` (Stage 05) decides; on approve the handler
  1. re-prices against today's price list and reports the delta to the approver,
  2. builds a sourcing plan (Stage 08c) across the companies the rep may sell for,
  3. takes the **credit hold** in the registry, then one **local reservation per sourcing company**, as
     saga legs that compensate in reverse on any failure (ADR-101, ADR-108, ADR-116),
  4. creates the sales order (Stage 14), which invoices through Stage 10c and splits per company.
  Any step failing compensates every completed step in reverse — releases, not deletions — and returns
  the pro forma to `Submitted` with the reason. A partially approved pro forma does not exist, and a
  crashed approval **resumes** from its intent rather than restarting.
- `IRepAvailabilityQuery` — group-wide *available*, per company, with `AsAt`, filtered to what the
  rep's visibility profile permits.
- `IRepPerformanceService` — snapshot a closed period; query a period with a comparison period and
  return both plus the variance.

**Infrastructure**
- Schema `fieldsales`. Number sequences `PF-` and `PFC-`, per company (Stage 06c's counters).
- Offline-first: the rep client captures against a local cache and replays through
  `POST /api/v1/sync/batches`, idempotent on `(tenant_id, source_node, operation_id)`. No bespoke
  replay endpoint (`PROGRESS.md` §4.11).
- Quartz job: expire pro formas, snapshot closed periods.

**API** — `/api/v1/field-sales/pro-formas`, `/pro-forma-credit-notes`, `/approvals`,
`/availability`, `/performance?period=&compareTo=`. Every endpoint in OpenAPI with examples.

**Permissions** — `fieldsales.proforma.capture/submit/view`, `fieldsales.proforma.approve`,
`fieldsales.performance.view.own`, `fieldsales.performance.view.team`, `fieldsales.cost.view`.

**Entitlement** — `FieldSales` module flag, gated through `IEntitlementService`. Metering: pro formas
captured, approvals, active reps.

## Business rules

1. A pro forma posts nothing and reserves nothing (ADR-107). Its availability figures are indicative
   and are labelled as such wherever they are shown.
2. Nothing becomes an invoice without an approval record — including via sync replay, the import
   pipeline, or any admin endpoint.
3. Approval holds credit, then reserves, then creates the order — each step local, each compensatable,
   the whole driven by a resumable intent. It never leaves stock held against nothing or credit consumed
   for an order that does not exist (ADR-108).
4. A pro forma past its expiry cannot be approved until it is re-priced.
5. A rep may only read customers, companies and figures their territory and visibility profile allow,
   enforced server-side.
6. A pro forma credit note approves into a Stage 10 credit note inside the original invoice's company.
   There is no cross-company credit note.
7. A performance snapshot for a closed period is immutable. A recomputation produces a new version
   with a reason, never a silent overwrite.
8. Replay of the same captured pro forma twice produces one document.

## Tests / acceptance

- Rep captures 20 hot plates offline, reconnects, replays twice → one pro forma.
- Approval where availability moved between capture and approval: the approver sees the delta, and the
  approval reserves only what exists, backordering the rest.
- Approval where the customer's **group** credit is exhausted across sister companies is refused with
  the group position in the error, and reserves nothing — the hold is never issued.
- Approval that dies after the reservation leg and before the order: re-running the intent completes it
  exactly once; abandoning it releases the reservations and expires the hold.
- Approval sources across two companies → one sales order, two invoices, reconciling to the pro forma.
- A rejected pro forma leaves zero reservations and zero postings — asserted by ledger and reservation
  counts, not by reading the code.
- Rep A cannot fetch rep B's customer, performance or a cost field. 403, not a filtered result.
- Performance: August vs July for one rep, per company and grouped, with variance; re-running the
  closed August produces the same figures.
- Coverage ≥ 80% on the stage's Domain + Application.

## Exit checklist

- [ ] `CLAUDE.md` §8 in full
- [ ] `field-sales-guard`, `stock-availability-guard`, `multi-company-guard`, `money-and-tax`,
      `sync-and-offline`, `architecture-guard` run, findings closed
- [ ] Approval policies registered with Stage 05's engine; no approval logic in this module
- [ ] Migration reversible, `Down` executed
- [ ] `docs/DATA_MODEL.md` §4o `fieldsales` filled in; replication registry updated
- [ ] Seed: two reps, a territory each, three pro formas — one approved into a split invoice, one
      rejected, one expired — and a closed-period performance snapshot
