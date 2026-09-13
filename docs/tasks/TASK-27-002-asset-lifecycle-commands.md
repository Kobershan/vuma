# TASK-27-002 — Asset lifecycle commands

**Status:** COMPLETE for lifecycle command slice · **Stage:** 27 · **Type:** Application, persistence

Implemented company-scoped commands and repository seams for fixed-asset creation, placement in
service, disposal, asset-book creation, and idempotent depreciation-run execution. Draft assets
cannot be disposed before capitalization; all handlers require the active company and reject
cross-company records. The unique asset-book-period key makes a retry return the existing run.

Evidence: the asset lifecycle invariant test passes, the full unit suite passed 1,466/1,466 before
this test addition, and Stage 27 PostgreSQL migration up/down passes. Depreciation posting,
proceeds, and complete API acceptance remain open Stage 27 work.
