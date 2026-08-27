# ARCHITECTURE — Vuma Retail

This document is the architecture map, not a second detailed specification. It answers where a
rule or contract lives so a task session can load only what it needs.

## Product boundary

Vuma is a Windows-installed retail ERP and POS platform. The store server is authoritative for
store trading; the cloud is authoritative for tenant roll-up and backup. Terminals must continue
selling offline and replay through the sync boundary when connectivity returns.

## Runtime topology

```text
WPF terminals + SQLite cache
          │ HTTPS/LAN + SignalR
          ▼
Store Server + PostgreSQL + outbox/inbox
          │ mTLS/JWT, batched and resumable
          ▼
Cloud API + PostgreSQL replica + encrypted object backups
          ▲
          │ REST/push
Android administration and public API clients
```

## Code boundaries

```text
Domain → Application → Infrastructure → hosts
                       ├── StoreServer
                       ├── CloudApi
                       ├── PublicApi
                       └── Web/API composition
```

- `VumaRetail.Domain` owns entities, value objects, invariants, and domain events. It has no
  infrastructure dependency.
- `VumaRetail.Application` owns commands, queries, ports, validators, and use-case orchestration.
- `VumaRetail.Contracts` owns transport DTOs and shared contract shapes.
- `VumaRetail.Infrastructure` owns EF Core, PostgreSQL mappings, repositories, persistence,
  messaging, sync infrastructure, and external adapters.
- `VumaRetail.Web` owns API composition, authentication edges, authorization filters, diagnostics,
  and OpenAPI exposure.
- Feature modules (`Finance`, `Licensing`, `Sync`, `Imports`, `Hardware`) own their own domain or
  application responsibilities and must use published ports at module boundaries.
- `StoreServer`, `CloudApi`, and `PublicApi` are hosts; they do not become alternate domain layers.

## Non-negotiable architectural rules

The detailed rules remain authoritative in the linked documents:

| Concern | Authority |
|---|---|
| Product requirements and locked stack | `CLAUDE.md` §§3–4 |
| Code conventions and project boundaries | `docs/CONVENTIONS.md` |
| Persistence, tenancy, schemas, and database rules | `docs/DATA_MODEL.md`, `docs/MULTI_COMPANY.md` |
| API shape, auth, errors, idempotency, and OpenAPI | `docs/API_STANDARDS.md` |
| Offline sync, outbox/inbox, and backup | `docs/SYNC_AND_BACKUP.md` |
| Security, POPIA, secrets, and vendor access | `docs/SECURITY.md` |
| Licensing and enforcement | `docs/LICENSING.md` |
| Testing and validation requirements | `docs/TESTING.md` |
| Accepted architectural rationale | `docs/DECISIONS.md` |
| Strategic delivery order | `docs/ROADMAP.md` |
| Stage scope and acceptance | `docs/stages/` |

## How to use this map

Start with `CLAUDE.md` and `docs/CURRENT.md`, then follow the current task's `Relevant references`.
Do not read every linked document by default. If a task changes an architectural boundary, add or
supersede an ADR before changing code.
