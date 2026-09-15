# TASK-23-001 — Service ticket, warranty snapshot, and custody foundation

**Status:** COMPLETE for ticket/warranty/custody foundation slice · **Stage:** 23 · **Part:** 23-P01

## Evidence

- `ServiceTicket` enforces tenant/company/customer identity and prevents closing an unresolved
  ticket except through the explicit waiting-for-customer or resolved states.
- `WarrantyClaim` retains the original sale reference, sale date, and serial snapshot; approval
  refuses a different serial and is single-decision.
- `ServiceCustodyEvent` is a separate append-only customer-goods record and therefore cannot inflate
  company-owned inventory or valuation.
- Focused Release unit tests: **3/3 passed** (`ServiceDomainTests`).
- Repair jobs now require an explicit start before completion, and service-part usage is immutable,
  operation-keyed, company-scoped, and exposes calculated total cost. Focused tests: **5/5 passed**.

## Remaining

Add custody export controls and real PostgreSQL tenant/company isolation evidence. SLA policy creation
is now covered by TASK-23-004.

Persistence mapping and migrations `20260913163837_Stage23_ServiceManagement` and
`20260913164736_Stage23_ServiceTicketOperationId` are now present; the PostgreSQL Up/Down test
passes **1/1** against the local disposable PostgreSQL database, including the operation-key column
rollback.

The application port/repository and first scoped commands are wired into both API hosts. The
service-filtered unit suite passes **23/23** and the StoreServer Release build has **0 errors**.
Service routes for ticket opening, warranty submission/approval, and repair opening/completion are
mapped in StoreServer and CloudApi. Real-host OpenAPI plus migration checks pass **2/2**.
Ticket closure is also exposed through a company-scoped command and permissioned route; the
real-host OpenAPI contract test passes with that route included.
Scoped ticket and custody list queries/GET routes now enforce the active company boundary. Migration
`20260913171313_Stage23_ServiceCustodyEvents` adds custody persistence; the expanded migration and
OpenAPI verification passes **2/2** on PostgreSQL.
Service-part consumption is now reserved, issued through the shared stock ledger, consumed against the
hold, and protected by operation replay checks; parts integration remains subject to full end-to-end
PostgreSQL availability and financial acceptance.
Replay handling now validates the active company context before looking up an existing ticket or
service-part usage, closing a same-company replay bypass; the service command suite passes **4/4**.

2026-09-15: Added the high-risk `service.custody.export` permission and a company-scoped CSV
custody export route. The web Release build succeeds with 0 errors and the permission catalogue
suite passes **9/9**; deterministic CSV escaping/column-order coverage passes **1/1**. PostgreSQL
tenant/company export isolation remains a Stage 23 acceptance gate.
