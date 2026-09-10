# TASK-22-10 — Batch-aware pick, ship and in-transit bucket

**Depends on:** 22-09, 13

Carry batch, expiry and serial identities. Fix transfer cost at ship time; exclude InTransit from both parties on-hand.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

