# Blockers

- 2026-09-18 — Phase 4 offline sale replay cannot be completed safely from the current contracts: the desktop is REST-only by architecture, while `/api/v1/sync/batches` accepts only registered replicated entity snapshots and rejects command envelopes; implementing terminal sale command replay requires an agreed server-side command-batch contract or a local domain/EF cache, neither of which exists in the repository.
- 2026-09-18 — Phase 0.9 has no checked-in WiX/Velopack project or bootstrapper configuration to migrate; the existing CI desktop and server publishes are self-contained win-x64 artifacts, so runtime bundling is covered, but installer prerequisite/atomic-update wiring requires the missing installer source.
