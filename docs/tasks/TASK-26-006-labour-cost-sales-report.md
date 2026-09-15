# TASK-26-006 — Labour cost versus sales report

Status: COMPLETE for the repository-deliverable report slice  
Stage: 26  
Type: Application/API and tests

## Objective

Provide a company-scoped workforce report comparing closed attendance hours and contract cost with
the invoiced sales analytics read model.

## Acceptance and verification

- The active company and tenant boundaries are enforced for employees and analytics rows.
- Hours are calculated from the existing immutable attendance/payroll calculator.
- The applicable employment contract supplies the hourly rate and currency.
- Revenue is read from all-category sales rows and grouped by currency; the percentage is zero when
  sales are zero.
- `GET /api/v1/workforce/labour-cost` is protected by `workforce.view`.

2026-09-15: `WorkforceLabourCostQueryTests` passes **2/2** and the Ecommerce API OpenAPI contract
assertion includes the new route. External payroll, specialist and full-stage compliance acceptance
remain follow-up work.
