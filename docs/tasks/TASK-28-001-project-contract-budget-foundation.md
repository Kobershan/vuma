# TASK-28-001 — Project, contract and budget foundation

**Status:** COMPLETE for domain foundation slice · **Stage:** 28 · **Type:** Domain, tests

Implemented company-scoped project lifecycle, versioned budget approval, separate committed/actual
measures, contract variations, milestone approval/billing transitions, and application command
handlers for budget approval, variation approval, and milestone billing. Unapproved variations do
not affect contract value, and milestones cannot be billed before approval or twice.

Evidence: `ProjectDomainTests` **3/3 passed**. Persistence, finance/workforce/procurement integration,
API, replay and company-isolation acceptance remain open.
