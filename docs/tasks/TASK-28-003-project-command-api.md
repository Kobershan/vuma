# TASK-28-003 — Project command API

**Status:** COMPLETE for API-surface slice · **Stage:** 28 · **Type:** API, authorization, build

Mapped project creation, budget approval, contract-variation approval and milestone billing under
`/api/v1/projects` on StoreServer and CloudApi. All routes require the projects module and
`projects.manage`; command handlers enforce the active company boundary.

Evidence: StoreServer and CloudApi Release builds pass with **0 errors**. Labour/procurement cost
allocation, WIP, rebates, invoice exactly-once integration and full API acceptance remain open.
