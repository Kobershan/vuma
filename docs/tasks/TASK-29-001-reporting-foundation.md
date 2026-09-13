# TASK-29-001 — Reporting definition and freshness foundation

**Status:** COMPLETE for domain foundation slice · **Stage:** 29 · **Type:** Domain, tests

Implemented publish/retire report definitions, monotonic generation/cursor checkpoints for replay-safe
projections, and dashboard snapshots that cannot report live when a contributor is stale. The report
definition query now exposes published definitions only; draft and retired definitions are unavailable
to the read API.

Evidence: `ReportingDomainTests` **4/4 passed**. Database projections, scoped APIs, exports, mobile
contracts and full acceptance remain open.
