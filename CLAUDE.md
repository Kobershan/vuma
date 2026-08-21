# CLAUDE.md — Vuma Retail

> **This file is the contract.** Read it fully at the start of every session, before touching any code.
> Vuma Retail is a Windows-installed retail ERP + POS platform with an in-store server, a cloud
> replica/backup tier, and an Android admin app. It is built stage-by-stage by autonomous Claude Code
> sessions. Each session picks up where the last one stopped by reading `docs/PROGRESS.md`.

---

## 0. Session protocol (do this every time, no exceptions)

**Both `docs/PROGRESS.md` and `docs/DECISIONS.md` are now lean, current-state-only files** (a
one-line-per-ADR index and an open-issues-only state file). Their full history lives in
`docs/archive/PROGRESS-ARCHIVE.md` and `docs/archive/DECISIONS-ARCHIVE.md` — **do not read the
archives unless you specifically need historical detail on a past stage.** Reading them by default is
the single biggest source of wasted tokens in this project's history; they are reference material, not
required reading.

```
1. Read  CLAUDE.md                      (this file)
2. Read  docs/PROGRESS.md               (current state, next stage, OPEN blockers only — short)
3. Read  docs/DECISIONS.md              (one-line ADR index — never re-litigate a LOCKED one)
4. Read  docs/stages/STAGE-NN-*.md      (the FIRST stage whose status is NOT_STARTED or IN_PROGRESS)
5. Read  ONLY the reference docs that stage lists under "Reference reading" — not the whole docs/ tree.
   For the long, module-organised references (`DATA_MODEL.md`, `SYNC_AND_BACKUP.md`), jump to the
   section(s) for the module your stage touches rather than reading the whole file — each has a
   navigation note at the top explaining its section layout.
6. EXECUTE the stage to completion, in THIS worktree, on a branch cut from an up-to-date `main`.
   Do not stop halfway. Do not ask the user anything. Do not start a second stage in a second
   worktree — see §1a.
7. Run   the stage's Exit Checklist. Every box must pass. Fix what fails and re-run.
8. Merge the stage's branch to `main` before ending the session. A stage is not done while it sits
   unmerged on a branch — see §1a.
9. Update docs/PROGRESS.md  (status, date, files touched, notes for the next session — keep it short;
   this is a state file, not a diary. Long narrative belongs in the archive, if anywhere.)
10. Append any new architectural choices to docs/DECISIONS.md as a one-line index entry, with the full
    Context/Decision/Consequences appended to docs/archive/DECISIONS-ARCHIVE.md.
11. Commit with:  feat(stage-NN): <stage title>
12. Stop. One stage is one session — do not pull the next stage into the same context even if there is
    room left. A fresh session with a fresh context window verifies the previous stage's docs are
    actually sufficient to resume from, which is the point.
```

**Universal kickoff prompt** (the user pastes this into any fresh chat, nothing else needed):

```
Read CLAUDE.md, docs/PROGRESS.md and docs/DECISIONS.md (not the archives) and follow the session
protocol in CLAUDE.md section 0.

Before picking anything new: if docs/PROGRESS.md's stage table shows a stage DONE but unmerged, or
IN_PROGRESS on another worktree, resolve that first (merge it, or tell me exactly why you can't) -
do not start a new stage while one is still open. See section 1a.

Then execute exactly ONE stage to completion - the first one whose status is NOT_STARTED or
IN_PROGRESS. Build it, verify it against its Exit Checklist, merge it to main, update PROGRESS.md
and DECISIONS.md, and commit. Then STOP - do not continue to the next stage even if you have context
left; a fresh session picking it up cleanly is the point, not a race to fit more in.

Do not ask me anything. Do not propose options or wait for approval - decide, record the decision as
an ADR if it's a real architectural choice, and keep going. If something is genuinely blocked (needs
a credential you don't have, needs Windows/Docker you don't have), stub it, log it under "Deferred" in
PROGRESS.md, and continue rather than stopping to ask.

Don't narrate as you go or summarize back to me what CLAUDE.md/PROGRESS.md already say - I've read
them. Just work. At the end, give me a short report: what shipped, what's still open, and the one
thing (if any) I need to decide before the next session.
```

---

## 1a. One stage, one worktree, one session — NON-NEGOTIABLE

This project previously ran multiple stages in parallel across separate git worktrees (e.g. Stage 05,
06 and 07 all mid-flight at once). **Stop doing this.** It does not produce more finished stages per
day — it produces multiple half-finished branches that then have to be rebased against each other,
which `docs/archive/PROGRESS-ARCHIVE.md` documents costing entire sessions on its own. It also means
you are paying for several concurrent sessions' token usage instead of one.

- **Exactly one stage is ever in progress at a time.** Before starting a new stage, confirm the
  previous stage's branch is merged to `main` and `docs/PROGRESS.md`'s stage-status table has no other
  row marked `IN_PROGRESS`.
- **Do not create a new worktree per stage.** Work directly on `main` (or a single short-lived branch
  merged back the same session). If you find leftover worktrees under `.claude/worktrees/` from past
  sessions, that is a sign this rule was broken before — flag it in `docs/PROGRESS.md`, do not add to it.
- **Target cadence:** one stage per session, 3–4 sessions per day, 21–28 stages per week. If a single
  stage is ballooning past that, it is a sign the stage is too large or the session is re-reading more
  than it needs — stop, write a handoff, and let the next session start clean rather than pushing
  through in the same context.
- **Model choice:** default to Sonnet (`scripts/run-autonomous.ps1`'s default). Opus costs several
  times more per token and this project's stage documents are already fully specified — most stages
  don't need Opus-tier judgement to execute correctly. Reach for `-Model opus` only for a specific
  stage you expect to be genuinely ambiguous, not as a standing default.

---

## 1. Autonomy policy — NON-NEGOTIABLE

**Sessions run unattended, overnight, with nobody watching.** Treat every ambiguity as a decision you
own. See `docs/AUTONOMOUS_OPERATION.md` for the environment setup.

- **Never call `AskUserQuestion`.** It is denied in settings, but do not reach for it. There is no one
  to answer.
- **Never write to `.claude/**`.** It is a protected path: writing there triggers a permission prompt
  that will stall an unattended run until morning. Nothing in this build needs to touch it.
- **Never leave the repo broken.** Commit at green checkpoints throughout a stage, not once at the end.
  A run that dies mid-refactor wastes the whole night.
- **If context is getting long, stop cleanly** at a green build with an honest handoff in
  `docs/PROGRESS.md` rather than pushing on with degraded judgement. The next invocation starts fresh.

- **Never ask the user a question.** Not for scope, not for naming, not for library choice, not for
  confirmation. If you are about to write "Would you like me to…", stop and just do it.
- **When ambiguous, choose** the option that is (a) consistent with `docs/DECISIONS.md`, (b) simplest
  to maintain, (c) least likely to need rework in a later stage. Then record it as an ADR in
  `docs/DECISIONS.md` with a one-line rationale.
- **Never mark a stage DONE that isn't.** If something genuinely cannot be completed (e.g. it needs a
  paid third-party account), stub it behind an interface, write a fake/in-memory implementation that
  satisfies the tests, log it in `docs/PROGRESS.md` under "Deferred — needs real credentials", and
  keep going. The build stays green.
- **Never delete or rewrite a completed stage's work** to make your own stage easier. Extend it.
- **Do not skip ahead.** Stages are ordered by dependency. If Stage 09 needs something Stage 06 was
  meant to build and it's missing, go back, finish it properly, and note the correction.
- **Secrets:** never commit real credentials. Use `appsettings.Development.json` (git-ignored) and
  `.env.example` with placeholder values.

---

## 2. Permissions

This project is intended to be run with full tool access so sessions never stall on a prompt:

```bash
claude --dangerously-skip-permissions
```

or, for unattended overnight runs, `scripts/run-autonomous.ps1`.

**Read `docs/AUTONOMOUS_OPERATION.md` before the first run** — there is one-time setup (user-scope
settings, and accepting the bypass dialog once interactively) without which prompts still appear and
background runs are refused.

**Run this on a dedicated dev machine, VM, or container** — bypass mode disables the checks that
normally protect the filesystem and shell, including protection against instructions hidden in files
Claude reads.

You are explicitly authorised to, without asking: create/modify/delete files anywhere under the repo,
run `dotnet`, `git`, `npm`, `psql`, `docker`, `gradle`, install NuGet/npm packages, generate and apply
EF Core migrations, run test suites, spin up Docker containers for Postgres/MinIO, write scripts, and
refactor anything you built in an earlier stage.

---

## 3. What we are building

**Vuma Retail** — a modular retail ERP with POS at its centre, deployed as a Windows application
against an in-store physical server, replicating to a cloud tier for backup, multi-store roll-up and
mobile access.

### Topology

```
┌──────────────────────── STORE (physical premises) ────────────────────────┐
│                                                                            │
│   POS Terminal (Win)     POS Terminal (Win)     Back-Office (Win)          │
│   Vuma Desktop         Vuma Desktop         Vuma Desktop             │
│   + SQLite offline cache + SQLite offline cache + SQLite offline cache     │
│          │                      │                      │                   │
│          └──────────────────────┴──────────────────────┘                   │
│                                 │  HTTPS/LAN + SignalR                     │
│                    ┌────────────▼─────────────┐                            │
│                    │  VUMA STORE SERVER     │  Windows Service           │
│                    │  ASP.NET Core API        │  (the store keeps trading  │
│                    │  PostgreSQL 16           │   even if internet is down)│
│                    │  Sync agent + Outbox     │                            │
│                    └────────────┬─────────────┘                            │
└─────────────────────────────────┼──────────────────────────────────────────┘
                                  │  mTLS + JWT, batched, resumable
                    ┌─────────────▼──────────────┐
                    │  VUMA CLOUD              │
                    │  ASP.NET Core API          │
                    │  PostgreSQL (replica of    │
                    │   all stores, tenant-keyed)│
                    │  S3-compatible backup vault│
                    │  (encrypted snapshots)     │
                    └─────────────┬──────────────┘
                                  │  HTTPS REST + push
                    ┌─────────────▼──────────────┐
                    │  VUMA ADMIN (Android)    │
                    │  Dashboards, approvals,    │
                    │  stock lookup, alerts      │
                    └────────────────────────────┘
```

### Non-negotiable product requirements

| # | Requirement |
|---|---|
| R1 | **POS never stops.** Sales complete offline; queue and replay on reconnect. |
| R2 | **Store server is the source of truth for the store**; cloud is the source of truth for the tenant/roll-up. |
| R3 | **Everything is API-first.** No feature exists in the desktop UI that is not reachable via the versioned REST API. |
| R4 | **Cloud backup is continuous and restorable.** "Store burns down" → new box, run restore, back trading. Verified by an automated DR drill in Stage 31. |
| R5 | **Ingest from Excel/CSV/PDF** for suppliers, customers, inventory and specials — with mapping, preview, validation and rollback. |
| R6 | **Full audit trail.** Who changed what, when, from which terminal — immutable. |
| R7 | **Modular licensing.** Each module can be switched on/off per tenant without breaking the rest. |
| R8 | **Multi-store, multi-warehouse, multi-currency, multi-tax** from the data model up, even if v1 ships single-store. |
| R9 | **Licensed SaaS on a recurring subscription.** Mandatory monthly signed licence, hardware-bound activation, entitlement gating, usage metering. A lapsed subscription drops the tenant to **read-only**: full read, report, reprint and export; no writes, including no sales. It must only ever be triggered by a known subscription state after completed dunning — never by a network fault, a vendor-side outage, a single failed charge or a hardware change. See `docs/LICENSING.md`. |
| R10 | **Telemetry is counts and health only.** No customer names, sales detail, employee data or document content ever leaves a tenant's premises for vendor purposes. Vendor staff have no path to tenant business data without a tenant-granted, time-boxed, audited support grant. |

---

## 4. Locked technology stack

Do not substitute these. If you believe one is wrong, write an ADR arguing it and get it into
`docs/DECISIONS.md` *before* changing code — but the default answer is "keep it".

| Layer | Choice |
|---|---|
| Language / runtime | C# 13 on .NET 9 (LTS-track), `net9.0-windows` for desktop |
| Desktop app | WPF + MVVM via `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Hosting` generic host |
| Desktop UI kit | WPF-UI (Fluent) + custom Vuma theme; touch-first POS layouts |
| Store server | ASP.NET Core 9 Minimal APIs + Windows Service host |
| Cloud API | ASP.NET Core 9, container-deployable (Docker) |
| Database | PostgreSQL 16 (store + cloud), schema-per-module |
| Terminal-local store | SQLite (`Microsoft.Data.Sqlite`) — offline cache + outbound queue |
| ORM | EF Core 9 + Npgsql; migrations checked into `src/VumaRetail.Infrastructure/Migrations` |
| IDs | **UUID v7** everywhere (sortable, offline-safe generation) |
| Auth | ASP.NET Core Identity + JWT (15 min access / 30 day rotating refresh), device certs for terminals, 4–8 digit PIN for POS operators |
| Realtime | SignalR (store LAN + cloud push) |
| Background work | `IHostedService` + Quartz.NET for schedules |
| Messaging pattern | Transactional Outbox + Inbox (idempotent) for all sync |
| Validation | FluentValidation |
| Mapping | Mapperly (source-generated, no runtime reflection) |
| Logging | Serilog → rolling file + OpenTelemetry OTLP → cloud |
| PDF / receipts | QuestPDF |
| Excel | ClosedXML (read + write) |
| PDF ingest | PdfPig for text, Tesseract OCR fallback for scans |
| Object storage | S3-compatible via AWS SDK (works with AWS S3, Backblaze B2, Wasabi, MinIO) |
| Tests | xUnit + FluentAssertions + NSubstitute + Testcontainers (Postgres) + Bogus |
| Desktop UI tests | FlaUI |
| Auto-update | Velopack (desktop + store server) |
| Installer | Velopack bundle wrapped by a WiX v4 bootstrapper that provisions PostgreSQL + the Windows Service |
| Android app | Kotlin 2.x, Jetpack Compose, Retrofit + kotlinx.serialization, Room offline cache |
| CI | GitHub Actions (`build`, `test`, `migrate-check`, `package`) |

---

## 5. Repository layout

```
vuma/
├── CLAUDE.md                     ← you are here
├── README.md
├── .claude/
│   ├── settings.json             ← full-access permission config
│   └── agents/                   ← subagent definitions (see docs/AGENTS.md)
├── docs/
│   ├── PROGRESS.md               ← ★ THE STATE FILE. Lean — current stage + OPEN items only.
│   ├── DECISIONS.md              ← ADR index, one line per ADR. Never re-litigate a LOCKED one.
│   ├── archive/                  ← full session-log & ADR history — NOT required reading, on-demand only
│   │   ├── PROGRESS-ARCHIVE.md
│   │   └── DECISIONS-ARCHIVE.md
│   ├── ROADMAP.md                ← stage index
│   ├── ARCHITECTURE.md
│   ├── DATA_MODEL.md
│   ├── API_STANDARDS.md
│   ├── SYNC_AND_BACKUP.md
│   ├── IMPORT_PIPELINE.md
│   ├── SECURITY.md
│   ├── TESTING.md
│   ├── CONVENTIONS.md
│   ├── HARDWARE.md
│   ├── API_LOYALTY.md            ← public loyalty API contract
│   ├── API_ECOMMERCE.md          ← public storefront API contract
│   ├── AUTONOMOUS_OPERATION.md   ← how to run builds unattended without prompts
│   ├── DESIGN_SYSTEM.md          ← tokens, themes, components, voice
│   ├── API_CONNECT.md            ← supplier↔retailer trading network contract
│   ├── LICENSING.md              ← SaaS licence model, enforcement ladder, anti-piracy
│   ├── API_CONTROL_PLANE.md      ← device + vendor API contract
│   ├── AGENTS.md
│   └── stages/STAGE-00 … STAGE-31 (incl. 04b, 30b)
├── src/
│   ├── VumaRetail.Domain/          entities, value objects, domain events, no dependencies
│   ├── VumaRetail.Application/     use cases (commands/queries), ports, validators
│   ├── VumaRetail.Infrastructure/  EF Core, repositories, outbox, integrations
│   ├── VumaRetail.Contracts/       DTOs shared by API, desktop, Android
│   ├── VumaRetail.StoreServer/     ASP.NET Core API + Windows Service (in-store)
│   ├── VumaRetail.CloudApi/        ASP.NET Core API (cloud tier)
│   ├── VumaRetail.Sync/            sync protocol, HLC clock, conflict resolution
│   ├── VumaRetail.Desktop/         WPF shell: POS + Back Office modules
│   ├── VumaRetail.Imports/         Excel/CSV/PDF ingestion engine
│   ├── VumaRetail.Reporting/       QuestPDF documents + report queries
│   └── VumaRetail.Hardware/        printers, drawers, scanners, scales, payment terminals
├── android/                          Vuma Admin (Kotlin/Compose)
├── samples/storefront/               minimal reference storefront proving the public API works
├── tests/
│   ├── VumaRetail.UnitTests/
│   ├── VumaRetail.IntegrationTests/
│   ├── VumaRetail.SyncTests/
│   └── VumaRetail.UiTests/
├── deploy/                           docker-compose (dev), WiX, Velopack, DR scripts
└── scripts/                          dev bootstrap, seed, backup/restore, drill
```

---

## 6. Module map → stage map

| Module | Built in stage |
|---|---|
| POS system | **09** (till, tenders, receipts, cash-up, hardware), 10 (promotions, returns) |
| Sales management | **10** (price lists, promotions, returns), 10c (quotes, invoices, analytics — split by ADR-074) |
| Inventory management | 08 (ledger), 13 (bin-level) |
| **Finance / accounting (GL, AR, AP, banking, tax)** | **07** |
| HR management | 25 |
| Warehouse management | 13 |
| Procurement | 12 |
| Manufacturing | 17 |
| BOM setup | 16 |
| Order management | 14 |
| CRM | 19 |
| Service management | 23 |
| Logistics management | 24 |
| Workforce management | 26 |
| Marketing automation | 22 |
| **Loyalty programme + public API** | **20** (`docs/API_LOYALTY.md`) |
| **Ecommerce / storefront API + channels** | **21** (`docs/API_ECOMMERCE.md`) |
| **Merchandise planning, forecasting, MRP/DRP** | **15** |
| **Quality management** | **18** |
| **Fixed assets, maintenance, store operations** | **27** |
| **Projects, contracts, job costing** | **28** |
| **Workflow, approvals, notifications, documents** | **05** |
| API access | 03 (platform) + every module stage |
| Cloud backup / restore | 04, hardened in 31 |
| **Design system (Apple-inspired, dark + light)** | **08b** (`docs/DESIGN_SYSTEM.md`) |
| **Customer accounts, lay-by, stokvels** | **10b** (ADR-055 — *not* a Stage 09 addendum; see `ROADMAP.md`'s ordering notes) |
| **Supplier↔retailer network, in-app ordering and payment** | **21b** (`docs/API_CONNECT.md`) |
| **Licensing, activation, entitlements** | **04b** (`docs/LICENSING.md`) |
| **Vendor control plane, usage analytics, SaaS billing** | **30b** (`docs/API_CONTROL_PLANE.md`) |
| Android admin app | 29 (API), 30 (app) |
| Excel/PDF ingest | 11 |

---

## 7. Hard rules for code

1. **Layering:** `Domain` ← `Application` ← `Infrastructure` ← host projects. Domain references
   nothing. A compile error caused by a wrong-direction reference is the correct outcome — fix the
   design, don't add the reference.
2. **Every write goes through a command handler.** No EF `SaveChanges` from a controller/endpoint.
3. **Every table gets:** `id uuid`, `tenant_id uuid`, `store_id uuid?`, `created_at`, `created_by`,
   `updated_at`, `updated_by`, `row_version bytea`, `sync_state`. Enforced by an EF base entity.
4. **Money is `decimal(18,4)`** stored with an explicit currency code. Never `double`. Never a bare
   decimal without currency.
5. **Quantities are `decimal(18,6)`** with a unit-of-measure reference.
6. **Stock levels are never a mutable column.** They are the projection of an append-only
   `inventory.stock_ledger`. Correcting stock means writing a new adjustment entry.
7. **Financial documents are immutable once posted.** Amend via credit note / reversal.
8. **Nothing is hard-deleted.** `deleted_at` + `deleted_by`, filtered by a global query filter.
9. **All timestamps are `timestamptz` in UTC.** Convert at the edge only.
10. **Nullable reference types on**, warnings-as-errors on Domain/Application.
11. Public API surface documented with XML doc comments; every endpoint appears in OpenAPI.
12. **No module names a GL account.** Modules raise financial events; Stage 07's posting rules engine
    decides the accounts. Enforced by an architecture test.
13. **No module implements its own approval logic.** Everything goes through Stage 05's
    `IApprovalService`. Also enforced by an architecture test.
14. **The public API has its own DTOs.** `VumaRetail.PublicApi` may not reference internal contracts;
    cost, margin, supplier and other-customer data must be structurally unreachable, not merely filtered.
15. **Read-only is deliberate or it is a bug.** A lapsed subscription drops a tenant to read-only —
    that is the commercial model. But it may only ever be triggered by a *known* subscription state
    after completed dunning. A vendor-side outage must never restrict anyone; stores run on their
    existing lease to its natural expiry. Three things stay writable in read-only regardless: the
    payment and card-update screens, the flush of already-captured offline data, and backup. Stage 04b
    asserts all of it.
16. **Telemetry is whitelisted aggregate counters only.** Building a metering payload from a business
    table is a bug, and there is a test that catches it.
17. **No screen defines its own colours, spacing, type sizes or radii.** Everything comes from
    `design/tokens.json` via generated theme files. An architecture test scans for literal hex values.
18. **No data crosses a tenant boundary without an active, mutually accepted connection**, and then
    only the documents that connection created. A supplier never writes directly into a retailer's
    data — everything arrives as a proposal the retailer accepts.
19. Feature work is not done until it has tests (see `docs/TESTING.md` for the required ratio).

---

## 8. Definition of Done (applies to every stage)

- [ ] `dotnet build -c Release` — zero warnings in `Domain`/`Application`, zero errors anywhere
- [ ] `dotnet test` — all green, stage's new code ≥ 80% line coverage on Domain + Application
- [ ] EF migration generated, applied, and **reversible** (`Down` tested)
- [ ] Endpoints appear in `/openapi/v1.json` with examples and error responses
- [ ] Anything user-facing works offline or fails gracefully with a queued action
- [ ] Sync contract updated in `docs/SYNC_AND_BACKUP.md` if new entities are replicated
- [ ] Permissions for the new module registered in the RBAC catalogue
- [ ] Financial posting rules registered with Stage 07's engine wherever the stage moves money or value
- [ ] Approval policies registered with Stage 05's engine wherever the stage gates on a threshold
- [ ] Module entitlement flag declared in the module manifest and gated through `IEntitlementService`
- [ ] Usage counters for the module added to the daily metering rollup (counts only, no business data)
- [ ] Seed/demo data added so the module is demonstrable with `scripts/seed.ps1`
- [ ] `docs/PROGRESS.md` updated, ADRs appended, committed

---

## 9. Localisation defaults

Defaults are set for a South African retail deployment and are **configuration, not hard-code**:
locale `en-ZA`, currency `ZAR`, VAT `15%` inclusive pricing, timezone `Africa/Johannesburg`, date
`yyyy-MM-dd`, paper `A4` + `80mm` receipts, and privacy handling aligned to POPIA (see
`docs/SECURITY.md`). Every one of these must be changeable per tenant from the admin UI. Tax is a
rules engine, not a constant.
