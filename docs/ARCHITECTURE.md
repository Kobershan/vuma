# ARCHITECTURE MAP — Vuma Retail

This is the authoritative navigation map, not a replacement for detailed specifications. A fresh
session starts here after `CLAUDE.md` and `docs/CURRENT.md`, then follows only references named by
its task.

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

## Project and module map

| Project/component | Responsibility | Boundary rule |
|---|---|---|
| `VumaRetail.Domain` | Entities, value objects, invariants, domain events | No infrastructure, HTTP, host, or database dependency |
| `VumaRetail.Application` | Commands, queries, ports, validation, orchestration, permissions | Uses published ports; one use case owns one transaction boundary |
| `VumaRetail.Contracts` | Versioned transport DTOs and shared API shapes | Never exposes domain entities or persistence types |
| `VumaRetail.Infrastructure` | EF Core/PostgreSQL, migrations, repositories, outbox/inbox, adapters | Implements ports and owns database wiring/secrets |
| `VumaRetail.Finance` | GL, AR/AP, posting and reconciliation | Consumers publish events; no handler names GL accounts |
| `VumaRetail.Licensing` | Signed licence, entitlement, enforcement, metering | Capability gating; no network-fault trade lockout |
| `VumaRetail.Sync` | HLC, conflict policy, cursors, replication workers | Transactional outbox and idempotent inbox |
| `VumaRetail.Imports` | Parse, stage, validate, preview, commit, rollback | Staged data cannot affect live data before commit |
| `VumaRetail.Hardware` | Device and statutory-provider abstractions | Providers stay outside the core domain |
| `VumaRetail.Web` | API composition, auth edges, authorization, diagnostics, OpenAPI | HTTP boundary; calls Application ports |
| `VumaRetail.StoreServer` | In-store Windows service/API host | Store database is trading authority |
| `VumaRetail.CloudApi` | Cloud API, tenant roll-up, backup/replica ingress | Cloud is roll-up/backup authority |
| `VumaRetail.PublicApi` | Internet-facing storefront/loyalty/partner host | Separate auth, rate limits, DTOs, and exposure posture |
| WPF desktop / Android admin | Clients | API consumers; terminal SQLite is cache plus outbound queue |

## Data and runtime boundaries

- Tenant access is enforced through `ITenantContext`, claims, and authorization.
- The registry database owns company identity, encrypted connection metadata, lifecycle, groups,
  saga containers, and migration state. Each company database owns its books, stock, numbering,
  audit, outbox/inbox, and restore unit.
- There is no cross-database transaction, 2PC, or FDW. Cross-company writes are registry-coordinated
  saga legs; cross-company reads are explicit bounded fan-outs with per-company failures.
- Store Server is authoritative for store trading. Cloud receives batched, resumable replication and
  owns tenant roll-up and backup metadata.
- Terminals use SQLite offline caches and outbound queues; reconnect replays through idempotent sync.
- Staff use Identity/JWT, terminals use device certificates, and POS operators use PINs. Permissions
  are module-scoped claims and company access is checked at the operation boundary.
- Domain/Application unit, architecture, PostgreSQL integration, API contract, sync/offline, and
  FlaUI tests are distinct layers. See `docs/TESTING.md` for the required matrix.

## Dependency rules

1. Domain never depends on infrastructure or hosts.
2. Application depends on abstractions and published contracts, never concrete database/HTTP clients.
3. Modules communicate through ports, contracts, and domain events; no cross-module table joins.
4. A command handler opens one company context only. Cross-company work uses fan-out or saga legs.
5. API DTOs do not expose entities, secrets, connection strings, or out-of-scope tenant data.
6. Replicated state changes write an outbox record atomically; receivers are idempotent.

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
