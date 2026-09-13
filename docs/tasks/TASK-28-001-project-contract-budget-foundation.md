# TASK-28-001 — Project, contract and budget foundation

**Status:** COMPLETE for domain foundation slice · **Stage:** 28 · **Type:** Domain, tests

Implemented company-scoped project lifecycle, versioned budget approval, separate committed/actual
measures, contract variations, and milestone approval/billing transitions. Unapproved variations do
not affect contract value, and milestones cannot be billed before approval or twice.

Evidence: `ProjectDomainTests` **3/3 passed**. Persistence, finance/workforce/procurement integration,
API, replay and company-isolation acceptance remain open.
