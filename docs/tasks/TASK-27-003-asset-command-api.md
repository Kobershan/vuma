# TASK-27-003 — Asset lifecycle command API

**Status:** COMPLETE for API-surface slice · **Stage:** 27 · **Type:** API, authorization, build

Mapped company-scoped fixed-asset creation, placement, disposal, asset-book creation and
depreciation-period commands under `/api/v1/assets`. Every mutating route requires the assets module
and `assets.manage` permission; handlers retain the active-company check.

Evidence: StoreServer and CloudApi Release builds completed with **0 errors**. Finance posting,
maintenance, leases, checklist persistence and full PostgreSQL API acceptance remain open.
