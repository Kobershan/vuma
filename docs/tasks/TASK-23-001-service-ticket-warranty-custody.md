# TASK-23-001 — Service ticket, warranty snapshot, and custody foundation

**Status:** IN_PROGRESS · **Stage:** 23 · **Part:** 23-P01

## Evidence

- `ServiceTicket` enforces tenant/company/customer identity and prevents closing an unresolved
  ticket except through the explicit waiting-for-customer or resolved states.
- `WarrantyClaim` retains the original sale reference, sale date, and serial snapshot; approval
  refuses a different serial and is single-decision.
- `ServiceCustodyEvent` is a separate append-only customer-goods record and therefore cannot inflate
  company-owned inventory or valuation.
- Focused Release unit tests: **3/3 passed** (`ServiceDomainTests`).

## Remaining

Add persistence mappings and migration, scoped application commands and permissions, API contracts,
custody query/export controls, and real PostgreSQL tenant/company isolation evidence.
