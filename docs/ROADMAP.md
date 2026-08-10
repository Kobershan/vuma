# ROADMAP — Vuma Retail

The stage index. Stages are ordered by **dependency**, not by importance — do not skip ahead
(`CLAUDE.md` §1). Stage numbers here are authoritative; the module → stage mapping in `CLAUDE.md` §6
must agree with this table.

Each stage gets a document at `docs/stages/STAGE-NN-<slug>.md` before it is executed. Where the
document does not exist yet, the executing session writes it first — objective, deliverables,
business rules, tests/acceptance, exit checklist — using `STAGE-04b-licensing.md` as the template.

| Stage | Title | Depends on | Doc | Status |
|---|---|---|---|---|
| 00 | Foundation — solution skeleton, conventions, CI | — | ✅ | DONE |
| 01 | Persistence core — EF Core, base entity, migrations, audit | 00 | ✅ | DONE |
| 02 | Identity, RBAC, permission catalogue | 01 | ✅ | DONE |
| 03 | API platform — versioning, ProblemDetails, OpenAPI, CQRS pipeline | 02 | — | NOT_STARTED |
| 04 | Sync + cloud backup foundation — outbox/inbox, HLC, restore | 03 | — | NOT_STARTED |
| 04b | Licensing, activation & entitlement | 04 | ✅ | NOT_STARTED |
| 05 | Workflow, approvals, notifications, documents | 04b | — | NOT_STARTED |
| 06 | Master data — items, variants, barcodes, UoM, partners | 05 | — | NOT_STARTED |
| 07 | Finance — GL, AR, AP, banking, tax, posting rules engine | 06 | — | NOT_STARTED |
| 08 | Inventory — append-only stock ledger + balance projection | 07 | — | NOT_STARTED |
| 09 | POS core — sale, refund, cash-up, offline, lay-by, self-checkout | 08 | — | NOT_STARTED |
| 10 | Sales, pricing, promotions, tenders | 09 | — | NOT_STARTED |
| 11 | Import pipeline — Excel/CSV/PDF ingest | 10 | — | NOT_STARTED |
| 12 | Procurement | 11 | — | NOT_STARTED |
| 13 | Warehouse — bin-level inventory | 12 | — | NOT_STARTED |
| 14 | Order management | 13 | — | NOT_STARTED |
| 15 | Merchandise planning, forecasting, MRP/DRP | 14 | — | NOT_STARTED |
| 16 | BOM setup | 15 | — | NOT_STARTED |
| 17 | Manufacturing | 16 | — | NOT_STARTED |
| 18 | Quality management | 17 | — | NOT_STARTED |
| 19 | CRM | 18 | — | NOT_STARTED |
| 20 | Loyalty programme + public API | 19 | — | NOT_STARTED |
| 21 | Ecommerce / storefront API + channels | 20 | — | NOT_STARTED |
| 22 | Marketing automation | 21 | — | NOT_STARTED |
| 23 | Service management | 22 | — | NOT_STARTED |
| 24 | Logistics management | 23 | — | NOT_STARTED |
| 25 | HR management | 24 | — | NOT_STARTED |
| 26 | Workforce management | 25 | — | NOT_STARTED |
| 27 | Fixed assets, maintenance, store operations | 26 | — | NOT_STARTED |
| 28 | Projects, contracts, job costing | 27 | — | NOT_STARTED |
| 29 | Android admin API surface | 28 | — | NOT_STARTED |
| 30 | Android admin app (Kotlin/Compose) | 29 | — | NOT_STARTED |
| 30b | Vendor control plane, metering & SaaS billing | 30 | ✅ | NOT_STARTED |
| 31 | Hardening, installer, DR drill, release | 30b | — | NOT_STARTED |

Live status is tracked in `docs/PROGRESS.md`, not here. This table is the plan; that file is the
truth. If they disagree, `PROGRESS.md` wins and this table gets corrected.

## Ordering notes

- **07 before 08 before 09.** Finance sits ahead of Inventory and POS because those modules post to
  it (ADR-016). Building POS first means retrofitting the ledger into a live sale path.
- **05 before every module.** Approvals, notifications and documents are built once, up front, so
  later stages configure rather than build (ADR-019).
- **04b before 06.** Every module stage from 06 onward gates on entitlements and reports usage, so
  the entitlement choke point must already exist.
- **20 and 21 late but together.** Both sit on `VumaRetail.PublicApi` with its own DTOs and auth
  model (ADR-021), and 21 depends on 20's member identity.
- **31 last.** The DR drill (R4) must exercise a system that is actually complete.
