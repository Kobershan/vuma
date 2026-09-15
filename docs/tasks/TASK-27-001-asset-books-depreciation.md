# TASK-27-001 — Asset books and depreciation foundation

**Status:** COMPLETE for asset-books, idempotent depreciation, Finance-event foundation and
closed-period error propagation; full acceptance remains a Stage 27 follow-up

## Scope

Implement company-owned fixed assets, asset books, lifecycle transitions, and a currency-explicit
straight-line depreciation calculator with a residual-value floor.

## Evidence

- `src/VumaRetail.Domain/Assets/AssetModels.cs` contains `FixedAsset`, `AssetBook`, and
  `DepreciationCalculator`.
- `tests/VumaRetail.UnitTests/Assets/AssetTests.cs` proves no charge is produced after useful life
  and the net book value does not fall below residual value; the focused test passes **1/1**.
- Asset mutation handlers now validate active-company scope before loading assets; the expanded
  asset-focused suite passes **16/16**.
- `FinancialAssetDepreciationEventPublisher` raises `assets.depreciation.recorded` through
  `IFinancialEventPoster` with the named `Depreciation` amount; hosts without Finance receive the
  logging fallback. The publisher contract is covered by `AssetTests`.

## Remaining

PostgreSQL end-to-end journal acceptance, closed-period policy, and broader asset API acceptance.
