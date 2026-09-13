# TASK-28-002 — Append-only project cost entries and reversals

**Status:** COMPLETE for domain slice · **Stage:** 28 · **Type:** Domain, persistence, tests

Added company/project-scoped cost entries with explicit source references and an append-only reversal
factory. Reversing a cost creates a new negative entry linked to the original; the original record is
never mutated. Persistence mapping and repository support are included; finance allocation and API
acceptance remain open.

Evidence: focused reversal test passes; PostgreSQL migration-chain test passes with the cost table
present and cleanly removed when rolling back to Stage 29.
