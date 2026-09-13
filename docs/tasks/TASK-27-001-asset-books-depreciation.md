# TASK-27-001 — Asset books and depreciation foundation

**Status:** IN_PROGRESS

## Scope

Implement company-owned fixed assets, asset books, lifecycle transitions, and a currency-explicit
straight-line depreciation calculator with a residual-value floor.

## Evidence

- `src/VumaRetail.Domain/Assets/AssetModels.cs` contains `FixedAsset`, `AssetBook`, and
  `DepreciationCalculator`.
- `tests/VumaRetail.UnitTests/Assets/AssetTests.cs` proves no charge is produced after useful life
  and the net book value does not fall below residual value; the focused test passes **1/1**.

## Remaining

EF persistence/migration, period-level idempotency, finance posting, API routes, and full acceptance.
