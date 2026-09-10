# TASK-22-09 — Accept or decline and immediate source reservation

**Depends on:** 22-08, 08

On acceptance append source StockReservation before pick. Test concurrent till sale protection and replay safety.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

