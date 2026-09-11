# TASK-25-002 — Employee contracts

Adds immutable, tenant-scoped employment-term snapshots. Contract rows reference Employee by ID and
never by an EF relationship, preserving module extraction and the audit/replication boundary.

Acceptance: validated dates/rates/currency, append-only replication policy, unique live contract start
per employee, and `hr_management.employment_contracts` mapping.
