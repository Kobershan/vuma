# Architecture index

This is a navigation index, not a duplicate architecture specification. Workers read this file to
choose the smallest authoritative context, then follow the links relevant to their assigned task.

| Concern | Authority |
|---|---|
| System boundaries and project/module map | [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) |
| Dependency direction and coding rules | [`CLAUDE.md`](../CLAUDE.md), [`docs/CONVENTIONS.md`](CONVENTIONS.md) |
| Database ownership and data boundaries | [`docs/DATA_MODEL.md`](DATA_MODEL.md), [`docs/MULTI_COMPANY.md`](MULTI_COMPANY.md) |
| API boundaries and versioning | [`docs/API_STANDARDS.md`](API_STANDARDS.md), [`docs/API_CONTROL_PLANE.md`](API_CONTROL_PLANE.md) |
| Tenant/company isolation and security | [`docs/MULTI_COMPANY.md`](MULTI_COMPANY.md), [`docs/SECURITY.md`](SECURITY.md) |
| POS and offline/sync boundaries | [`docs/SYNC_AND_BACKUP.md`](SYNC_AND_BACKUP.md), [`docs/HARDWARE.md`](HARDWARE.md) |
| Integration and import boundaries | [`docs/API_CONNECT.md`](API_CONNECT.md), [`docs/IMPORT_PIPELINE.md`](IMPORT_PIPELINE.md) |
| Locked architectural decisions | [`docs/DECISIONS.md`](DECISIONS.md) |
| Stage scope, acceptance, and exit criteria | [`docs/ROADMAP.md`](ROADMAP.md), [`docs/stages/`](stages/) |
| Canonical executable task queues | [`docs/tasks/README.md`](tasks/README.md), stage `STAGE-*-INDEX.md` files |

The supervisor executes only task IDs linked by a canonical stage index. Historical `TASK-NNN` files,
planning notes, and future-stage documents remain useful references but are not executable queue entries.
