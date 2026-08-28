# Stage 06c task index — Multi-company foundation

This index decomposes Stage 06c into independently executable sessions. Execute in order; a task may
only start when its dependencies are green. The stage document remains the scope authority.

| Order | Task | Status | Depends on |
|---:|---|---|---|
| 1 | [TASK-005 — Registry database model and migration chain](TASK-005-stage-06c-registry-foundation.md) | COMPLETE | 01, 04, 06, 07 |
| 2 | [TASK-006 — Registry groups, saga intents, legs and outbox](TASK-006-stage-06c-registry-saga-records.md) | COMPLETE | 005 |
| 3 | [TASK-007 — Company connection resolver and cache invalidation](TASK-007-stage-06c-company-routing.md) | COMPLETE | 005 |
| 4 | [TASK-008 — Company context and one-company DbContext factory](TASK-008-stage-06c-company-context.md) | COMPLETE | 007 |
| 5 | [TASK-009 — Fan-out reads with per-company failures](TASK-009-stage-06c-fanout-reads.md) | COMPLETE | 008 |
| 6 | [TASK-010 — Provisioning lifecycle and resumability](TASK-010-stage-06c-provisioning.md) | COMPLETE | 006, 008 |
| 7 | [TASK-011 — Company deactivation and read-only behavior](TASK-011-stage-06c-deactivation.md) | COMPLETE | 010 |
| 8 | [TASK-012 — Migration fan-out and serving guard](TASK-012-stage-06c-migration-fanout.md) | COMPLETE | 005, 007, 010 |
| 9 | [TASK-013 — company_id retrofit across business tables](TASK-013-stage-06c-company-id-retrofit.md) | IN_PROGRESS | 008, 012 |
| 10 | [TASK-014 — Per-database backup and sync boundaries](TASK-014-stage-06c-backup-sync.md) | NOT_STARTED | 006, 008, 013 |
| 11 | [TASK-015 — Companies API and permissions](TASK-015-stage-06c-companies-api.md) | NOT_STARTED | 010, 011, 012 |
| 12 | [TASK-016 — Seed data and acceptance verification](TASK-016-stage-06c-seed-acceptance.md) | NOT_STARTED | 010, 011, 012, 013, 014, 015 |
| 13 | [TASK-017 — Architecture, multi-company and sync specialist review](TASK-017-stage-06c-specialist-review.md) | NOT_STARTED | 016 |
| 14 | [TASK-018 — Final Stage 06c verification and documentation](TASK-018-stage-06c-final-verification.md) | NOT_STARTED | 017 |

## Planning status conflicts

- `docs/CURRENT.md` and the old TASK-005 handoff reported `IN_PROGRESS` and described already-applied
  source changes.
- `docs/stages/STAGE-06c-multi-company.md` and `docs/PROGRESS.md` report Stage 06c `NOT_STARTED`.
- `docs/ROADMAP.md` says `PROGRESS.md` is authoritative for live stage status.
- This planning pass does not silently claim the prior source work is complete. The Stage 06c document
  records the conflict for review; the task files below describe the intended green implementation
  checkpoints and start at `NOT_STARTED`.
