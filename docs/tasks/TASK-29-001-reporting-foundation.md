# TASK-29-001 — Reporting definition and freshness foundation

**Status:** COMPLETE for domain foundation slice · **Stage:** 29 · **Type:** Domain, tests

Implemented publish/retire report definitions, monotonic generation/cursor checkpoints for replay-safe
projections, and dashboard snapshots that cannot report live when a contributor is stale.

Evidence: `ReportingDomainTests` **3/3 passed**. Database projections, scoped APIs, exports, mobile
contracts and full acceptance remain open.
