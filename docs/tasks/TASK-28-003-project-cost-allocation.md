# TASK-28-003 — Project cost allocation

Status: IN_PROGRESS  
Stage: 28  
Type: Application/API

## Objective

Allocate labour, procurement or other actual costs to a company-scoped project with replay-safe
source-reference identity.

## Scope

Project lookup and company authorization, immutable cost-entry creation, same-content idempotent
replay, changed-content conflict refusal, persistence port and API route.

## Verification

2026-09-13: Project-focused unit suite passes 3/3, including same-source idempotency and changed
content rejection. StoreServer build coverage includes the new application, repository and endpoint
surface.

## Follow-up findings

Labour/time-sheet and procurement adapters, Finance posting, budget measure updates, cross-company
sagas, job-cost reports and PostgreSQL/API integration acceptance remain open.

2026-09-13: Added a company-scoped `GET /api/v1/projects/{projectId}/costs/summary` query. It groups
immutable actual cost entries by currency, includes reversals, never combines currencies without FX,
and returns null for a cross-company project. Labour/procurement adapters, Finance posting, budget
measure updates, cross-company sagas and PostgreSQL/API acceptance remain open.

The summary route uses the new low-risk `projects.project.view` permission; project creation and
commitment approvals remain protected by high-risk `projects.project.manage`.
