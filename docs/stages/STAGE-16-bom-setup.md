# Stage 16 — BOM Setup

Status: DONE (2026-09-11)

Stage 16 establishes the manufacturing definition layer required by Stage 17. It owns versioned,
tenant-scoped bills of materials, alternate components, scrap factors, routings, and rolled-up
costing inputs. It does not execute production, consume stock, or post accounting entries.

## Task index

- `TASK-16-01` — BOM domain lifecycle and invariants — COMPLETE
- `TASK-16-02` — BOM persistence and migration — COMPLETE
- `TASK-16-03` — BOM application commands, queries, permissions, and API — COMPLETE
- `TASK-16-04` — BOM explosion, rolled-up costing, seed, and exit verification — COMPLETE

## Exit criteria

- [x] Domain lifecycle and invariant tests pass.
- [x] Real PostgreSQL migration Up/Down is verified (`ManufacturingMigrationTests`, 1/1, 2026-09-10).
- [x] Commands, queries, API, permissions, tenant/company isolation, sync, and metering are complete.
- [x] Multi-level explosion handles cycles, alternates, scrap, and quantity precision.
- [x] Rolled-up costing uses `Money` and rejects mixed currencies.
- [x] Demo seed, stage-specific architecture review, specialist review, and stage verification pass.
- [x] Stage is documented as `DONE` after evidence review.

Verification note (2026-09-11): focused manufacturing unit tests pass 13/13 and the real
PostgreSQL manufacturing integration suite passes 5/5, covering migration Up/Down reversibility,
authorized create/read/publish, permission denial, domain not-found handling, and OpenAPI route
publication. The migration snapshot alignment was reconciled without recreating existing BOM
tables. Demo seed, permissions, tenant scoping, replication metadata, explosion, and costing are
present and covered by the focused implementation/test review. The previously run global unit
suite passed 1371/1371; two global architecture rules remain unrelated to Stage 16. Stage 16 is
closed with those external project-level issues recorded outside this stage.
