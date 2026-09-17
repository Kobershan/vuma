# TASK-23-005 — Service warranty, repair and part reads

**Stage:** 23 — Service Management
**Status:** COMPLETE — verified (2026-09-18)

## Scope

Complete the company-scoped service read surface for warranty claims, repair jobs and issued part
usage. Every query establishes the active company before accessing its repository and filters the
loaded rows by the active tenant as a defence in depth against cross-scope data leakage.

## Implementation

- Added bounded repository reads for warranties, repairs and part usage.
- Added query DTOs/handlers with tenant and company filtering.
- Added `GET /api/v1/service/warranties`, `/repairs` and `/parts` routes using the view permission.
- Added unit coverage for tenant isolation, active-company refusal, and part quantity/currency
  preservation.

## Verification

Verification completed on 2026-09-18:

- Service unit tests pass **43/43**.
- StoreServer Release build passes with **0 warnings, 0 errors**.
- PostgreSQL-backed service migration, API contract, and custody-isolation tests pass **3/3**.

Invoicing/RMA integration and specialist review remain open Stage 23 work; this task's scoped read
surface is complete.
