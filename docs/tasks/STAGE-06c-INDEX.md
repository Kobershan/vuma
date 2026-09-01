# Stage 06c task index — Multi-company foundation

This legacy index is retained for history. The canonical executable index is the table below and in
the Stage 06c document. A task is `READY` only when every dependency is `COMPLETE`; no parallelism is
approved for this stage.

## Canonical queue

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| [TASK-06C-01](TASK-06C-01-registry-foundation.md) | DOMAIN / INFRASTRUCTURE / DATABASE | Registry foundation | — | READY |
| [TASK-06C-02](TASK-06C-02-registry-saga-records.md) | DOMAIN / INFRASTRUCTURE / DATABASE | Registry saga records | 06C-01 | NOT_STARTED |
| [TASK-06C-03](TASK-06C-03-company-routing.md) | APPLICATION / INFRASTRUCTURE / SECURITY | Company routing | 06C-01 | NOT_STARTED |
| [TASK-06C-04](TASK-06C-04-company-context.md) | APPLICATION / INFRASTRUCTURE / SECURITY / TEST | Company context | 06C-03 | NOT_STARTED |
| [TASK-06C-05](TASK-06C-05-fanout-reads.md) | APPLICATION / INTEGRATION / TEST | Bounded fan-out reads | 06C-04 | NOT_STARTED |
| [TASK-06C-06](TASK-06C-06-provisioning.md) | APPLICATION / INFRASTRUCTURE / DATABASE / SECURITY | Resumable provisioning | 06C-02, 06C-04 | NOT_STARTED |
| [TASK-06C-07](TASK-06C-07-deactivation.md) | APPLICATION / API / SECURITY / TEST | Deactivation | 06C-06 | NOT_STARTED |
| [TASK-06C-08](TASK-06C-08-migration-fanout.md) | INFRASTRUCTURE / DATABASE / APPLICATION / TEST | Migration fan-out | 06C-01, 06C-03, 06C-06 | NOT_STARTED |
| [TASK-06C-09](TASK-06C-09-company-id-retrofit.md) | DOMAIN / INFRASTRUCTURE / DATABASE / TEST | Company identity retrofit | 06C-04, 06C-08 | NOT_STARTED |
| [TASK-06C-10](TASK-06C-10-backup-sync.md) | INFRASTRUCTURE / INTEGRATION / SECURITY / TEST | Per-database backup/sync | 06C-02, 06C-04, 06C-09 | NOT_STARTED |
| [TASK-06C-11](TASK-06C-11-companies-api.md) | API / APPLICATION / SECURITY / TEST | Companies and migration API | 06C-06, 06C-07, 06C-08 | NOT_STARTED |
| [TASK-06C-12](TASK-06C-12-acceptance.md) | TEST / DOCUMENTATION | Acceptance evidence | 06C-01…06C-11 | NOT_STARTED |
| [TASK-06C-13](TASK-06C-13-specialist-review.md) | REVIEW | Specialist review | 06C-12 | NOT_STARTED |
| [TASK-06C-14](TASK-06C-14-stage-closure.md) | STAGE-CLOSURE / DOCUMENTATION / REVIEW | Stage closure | 06C-13 | NOT_STARTED |

Only TASK-06C-01 is deterministic READY.

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
