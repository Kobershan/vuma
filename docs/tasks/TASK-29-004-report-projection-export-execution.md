# TASK-29-004 — Replay-safe projection and export execution slice

**Status:** COMPLETE for projection/renderer/executor slice · **Stage:** 29 · **Type:** Domain, application, infrastructure, tests

Added a company-scoped replay-safe reporting projection that deduplicates event IDs, advances a
monotonic generation/cursor checkpoint, keeps currencies in separate measure keys, and can rebuild
from the ordered contribution stream. Added deterministic UTF-8 CSV rendering with RFC 4180
escaping, a path-safe filesystem artifact store, and a queued export executor that renders, stores,
completes an export for the surrounding command/worker transaction or records a bounded failure
reason; it deliberately leaves the commit to the shared unit-of-work boundary.

Evidence: reporting unit suite **16/16 passed**. Provider-specific projection adapters, durable
scheduled-report records, queued export polling/host registration, and production object-store
configuration remain open because their source contracts and deployment credentials are not present
in this repository slice.
