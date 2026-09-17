# TASK-23-005 — Service warranty, repair and part reads

**Stage:** 23 — Service Management
**Status:** COMPLETE (2026-09-18)

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

The targeted Service unit suite and StoreServer Release build are required for closure. Full
PostgreSQL service API/isolation acceptance, invoicing/RMA integration and specialist review remain
open Stage 23 work.
