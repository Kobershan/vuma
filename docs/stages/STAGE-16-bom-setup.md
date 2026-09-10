# Stage 16 — BOM Setup

Status: IN_PROGRESS

Stage 16 establishes the manufacturing definition layer required by Stage 17. It owns versioned,
tenant-scoped bills of materials, alternate components, scrap factors, routings, and rolled-up
costing inputs. It does not execute production, consume stock, or post accounting entries.

## Task index

- `TASK-16-01` — BOM domain lifecycle and invariants — COMPLETE
- `TASK-16-02` — BOM persistence and migration — NOT_STARTED
- `TASK-16-03` — BOM application commands, queries, permissions, and API — IN_PROGRESS
- `TASK-16-04` — BOM explosion, rolled-up costing, seed, and exit verification — IN_PROGRESS

## Exit criteria

- [x] Domain lifecycle and invariant tests pass.
- [ ] Real PostgreSQL migration Up/Down is verified.
- [ ] Commands, queries, API, permissions, tenant/company isolation, sync, and metering are complete.
- [x] Multi-level explosion handles cycles, alternates, scrap, and quantity precision.
- [x] Rolled-up costing uses `Money` and rejects mixed currencies.
- [ ] Demo seed, architecture checks, full suite, specialist review, and stage verification pass.
- [ ] Stage is documented as `DONE` only after every checklist item is evidenced.
