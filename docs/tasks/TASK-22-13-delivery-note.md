# TASK-22-13 — Transfer delivery note and compliance hook

**Depends on:** 22-10, 22-11

Generate printable/exportable notes with SKU, quantity, batch/expiry, sender, receiver, date and driver reference. Never issue a VAT invoice.

**Acceptance:** implementation follows ADR-099 and ADR-116, uses the existing registry/company context seams, preserves append-only audit and includes focused domain/application/integration tests.

