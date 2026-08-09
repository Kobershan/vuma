# PROGRESS — Zenith Retail

> ★ **THE STATE FILE.** Read first, write last. This file is the truth about where the build is;
> `ROADMAP.md` is only the plan. If they disagree, correct the roadmap.

**Last updated:** 2026-08-09 · **Current stage:** 02 — Identity, RBAC, permission catalogue · **Status:** NOT_STARTED

---

## 1. Stage status

| Stage | Title | Status | Changed |
|---|---|---|---|
| 00 | Foundation — solution skeleton, conventions, CI | **DONE** | 2026-08-09 |
| 01 | Persistence core — EF Core, base entity mapping, migrations, audit | **DONE** | 2026-08-09 |
| 02 | Identity, RBAC, permission catalogue | NOT_STARTED | — |
| 03 – 31 | see `ROADMAP.md` | NOT_STARTED | — |

---

## 2. Session log

### 2026-08-09 — Repository initialised, documentation set filed

Loose specification documents were dropped into the working directory root. This session filed them
into the `CLAUDE.md` §5 layout, wrote the missing scaffolding documents, and connected the repo to
GitHub.

**Filed (moved, content unchanged except where noted):**

| From | To |
|---|---|
| `DECISIONS.md` | `docs/DECISIONS.md` |
| `TESTING.md` | `docs/TESTING.md` |
| `LICENSING.md` | `docs/LICENSING.md` |
| `API_CONTROL_PLANE.md` | `docs/API_CONTROL_PLANE.md` |
| `STAGE-04b-licensing.md` | `docs/stages/STAGE-04b-licensing.md` |
| `STAGE-30b-control-plane.md` | `docs/stages/STAGE-30b-control-plane.md` |

`CLAUDE.md` stays at the root, as §5 requires.

**Created:** `README.md`, `.gitignore`, `.claude/settings.json`, `docs/ROADMAP.md`,
`docs/AGENTS.md`, `docs/PROGRESS.md` (this file), and six subagent definitions under
`.claude/agents/`.

**Corrections made to the source documents:**

1. `CLAUDE.md` §6 contained a **duplicated, stale module map** appended after the live one, with
   conflicting stage numbers for the same modules (POS at 07 vs 09, Excel/PDF ingest at 09 vs 11,
   backup hardening at 23 vs 31). It was a leftover from the revision that inserted Finance at 07
   and pushed everything down. The stale block was deleted; the live map is authoritative and now
   agrees with `ROADMAP.md`.
2. `docs/stages/STAGE-04b-licensing.md` listed `ADR-023` as reference reading. ADR-023 is superseded
   (→ ADR-027 → ADR-028). Pointed at ADR-028 with an explicit note that 023 and 027 must not be
   implemented.
3. `docs/TESTING.md` §7 attributed the no-accidental-lockout suite to ADR-027, also superseded.
   Reworded to note the guarantees carry forward into ADR-028.

### 2026-08-09 — Stage 00 complete: foundation

`dotnet build -c Release` → **0 warnings, 0 errors**. `dotnet test` → **55 passed, 0 failed**
(46 unit, 9 architecture). Full exit checklist in `docs/stages/STAGE-00-foundation.md`.

**Build configuration.** `ZenithRetail.sln`; `global.json` pinning the 9.0.100 SDK band;
`Directory.Build.props` (net9.0, C# 13, nullable, deterministic, `TreatWarningsAsErrors` on Domain and
Application only); `Directory.Packages.props` (central package management, ADR-030); `.editorconfig`.

**Projects created** — cross-platform only, per ADR-031:
`Domain`, `Application`, `Contracts`, `Infrastructure`, `Sync`, `StoreServer`, `CloudApi`,
`PublicApi`, plus `tests/ZenithRetail.UnitTests` and `tests/ZenithRetail.ArchitectureTests`.
Project references encode the layering, so a wrong-direction reference is a compile error.

**Domain primitives.** `Entity` (the §7 rule 3 mandatory columns, soft delete, audit stamping);
`UuidV7` (RFC 9562, monotonic counter, backwards-clock safe); `Money` (`decimal(18,4)` + mandatory ISO
currency, cross-currency arithmetic throws); `Quantity` (`decimal(18,6)` + UoM); `SyncState` and
`ConflictPolicy`.

**Application abstractions.** `ICommand`/`IQuery`/handlers/`IDispatcher` (ADR-009), and the
`CommandSideEffect` classification with `ReadOnlyExemption` that Stage 04b's read-only interceptor
depends on (ADR-034).

**Enforcement is proven, not assumed.** Both mechanisms were deliberately broken and the break
confirmed before reverting: an unclassified command failed the architecture test by name, and a
`Domain → Application` reference failed the build outright.

**Also added:** `.github/workflows/ci.yml` (build → test → architecture-tests → vulnerability-scan,
with `migrate-check` and `package` declared as gates that pass trivially until Stages 01 and 31 fill
them in), and `docs/CONVENTIONS.md`.

**ADRs appended:** ADR-030 central package management · ADR-031 Windows-only projects deferred to
their stage · ADR-032 FluentAssertions pinned to the Apache-2.0 6.x line · ADR-033 money rounds
midpoints away from zero · ADR-034 mandatory command side-effect classification.

**Toolchain change:** the .NET 9 SDK (9.0.316) was installed to `~/.dotnet` on this machine and made
discoverable three ways — a `~/.local/bin/dotnet` symlink (which `~/.profile` already puts on `PATH`),
a `DOTNET_ROOT` + `PATH` export in `~/.bashrc`, and a git-ignored `.vscode/settings.json` pointing the
.NET Install Tool at it. The last one matters because C# Dev Kit was otherwise acquiring a
**runtime-only .NET 10** and reporting "No installed .NET SDK was found on the computer", which leaves
the IDE with no build host. **A VS Code window reload is needed for it to take effect.**

### 2026-08-09 — Stage 01 complete: persistence core

`dotnet build -c Release` → **0 warnings, 0 errors**. `dotnet test` → **127 passed, 0 failed**
(78 unit, 15 architecture, 34 integration), stable across three consecutive runs. Domain +
Application line coverage **94.9%**. Full exit checklist in `docs/stages/STAGE-01-persistence.md`.

**The point of this stage is what it made impossible.** A module author can no longer write an
un-audited row, see another tenant's data by forgetting a `Where`, hard-delete anything, edit an
immutable record, call `SaveChanges` from an endpoint, read the wall clock, draw a foreign key across
a module boundary, or add an entity without saying how it replicates. Each is a build break or a
failing test, not a review comment.

**Application ports.** `IClock` (the only source of time), `IPrincipalAccessor` (who is acting, and
from which terminal — R6's "who"), `ITenantContext` (the ambient tenant, plus a deliberately awkward
`BypassTenantFilter(reason)` for the sync receiver and the backup job), `IUnitOfWork` (the boundary
the Stage 03 pipeline will drive, and the one ADR-006's outbox write has to share).

**Domain — `platform` module.** `Tenant` (isolation root; its `TenantId` is its own `Id`, so the
query filter needs no special case), `Store` (R8 from the first migration), `AuditEntry`
(append-only, R6). Plus `IImmutableRecord` and `[Replicated(scope, policy)]` — both markers the
persistence layer enforces centrally.

**Infrastructure.** `ZenithRetailDbContext` with schema-per-module and `snake_case` by convention;
`EntityConfiguration<T>` mapping the §7 rule 3 columns exactly once; both global query filters
applied to every entity by reflection so none can opt out; `ValueObjectMapping` fixing the shape of
money and quantity columns before Stage 07 needs them; a single `AuditInterceptor` doing soft-delete
rewriting, the immutability guard, audit stamping and the trail write in one pass with an explicit
order. `InitialCreate` migration, `Down` genuinely exercised.

**Tests.** New `tests/ZenithRetail.IntegrationTests` against real PostgreSQL with real migrations,
isolated per test by cloning a migrated template database. Four new architecture rules, each proven
by a deliberate violation before being reverted.

**ADRs appended:** ADR-035 application-generated `bytea` concurrency token (not `xmin`, because
terminals run SQLite) · ADR-036 integration tests bind to a connection string, Testcontainers being
one supplier of it · ADR-037 Tenant and Store are platform entities built here.

**Correction to Stage 00.** `UuidV7Tests.Encodes_the_supplied_timestamp_and_reads_it_back` was
order-dependent — `UuidV7` keeps a process-wide monotonic high-water mark, so the encoded timestamp
is `max(supplied, high-water)` and a fixed literal passed or failed depending on what else had run.
The production behaviour is correct; the test now derives its instant from the current mark.

**Toolchain.** Docker is still not installed, and it turned out not to be needed:
`scripts/pg-test.sh` starts a throwaway PostgreSQL cluster from the server binaries already on this
machine (PostgreSQL 18), on port 55432, with no Docker and no sudo. `scripts/test.sh` wraps the whole
suite in it. `dotnet-ef` 9.0.0 installed as a global tool.

---

## 3. Deferred — needs real credentials

Nothing yet. Stage 04b (licence signing key / KMS) and Stage 30b (payment gateway) will be the
first entries.

---

## 4. Blockers and known gaps

### 4.1 Documents referenced by `CLAUDE.md` §5 that do not exist yet

These are cited as reference reading by stages that will need them. Each should be written by the
stage that first depends on it, not all up front:

`ARCHITECTURE.md` · `API_STANDARDS.md` (Stage 03) · `SYNC_AND_BACKUP.md` (Stage 04) ·
`IMPORT_PIPELINE.md` (Stage 11) · `SECURITY.md` (Stage 02) · `HARDWARE.md` (Stage 09) ·
`API_LOYALTY.md` (Stage 20) · `API_ECOMMERCE.md` (Stage 21) · `GAP_ANALYSIS.md`.

Written so far: `CONVENTIONS.md` (Stage 00), `DATA_MODEL.md` (Stage 01).

Stage documents exist for **00**, **01**, **04b** and **30b**. Every other stage document must be
written by the session that executes it, using `STAGE-04b-licensing.md` as the template.

### 4.2 Unresolved contradiction in `docs/TESTING.md` §7 — resolve during Stage 04b

The licensing test suite in TESTING.md §7 is still written in the **lockout** language of the
superseded ADR-027: "the store trades normally throughout, and locks only at the configured
boundary", "Emergency access code: unlocks fully", "Open-session carve-out ... then the terminal
locks". ADR-028 is the live decision and it ends the ladder at **read-only**, with hard lockout
surviving only as a manual, two-person, audited vendor action for confirmed abuse.

The *intent* of every listed test survives — they all assert that restriction cannot happen by
accident — but the assertions need restating against read-only rather than lockout. This was left
for Stage 04b rather than rewritten here, because it is a test-design decision, not a filing error.
`docs/LICENSING.md` §4 should be re-read at the same time for the same drift.

### 4.3 Toolchain not available on the current development machine

This repository is being developed on Linux. The product targets Windows.

| Need | Status | Consequence |
|---|---|---|
| .NET 9 SDK | **installed** 2026-08-09 — 9.0.316 in `~/.dotnet`, on `PATH` via `~/.local/bin` and `~/.bashrc` | build and test verified locally |
| `dotnet-ef` | **installed** 2026-08-09 — 9.0.0 global tool, `~/.dotnet/tools` | migrations scaffold and apply |
| PostgreSQL | **installed** — server 18.4, `/usr/lib/postgresql/18/bin` | `scripts/pg-test.sh` starts a throwaway cluster on port 55432 for the integration tests. **No longer blocking** |
| Docker | **not installed** | Testcontainers is the preferred supplier when present but is no longer required (ADR-036). Worth installing eventually so CI and local use the identical path |
| Windows | n/a — Linux | `ZenithRetail.Desktop` (WPF), `.Hardware` and the FlaUI UI tests cannot build or run here at all. Blocks Stage 09 onward (ADR-031) |

**Resolved during Stage 01.** Docker was expected to block this stage's exit checklist. It did not:
the machine already had a full PostgreSQL server install, and `scripts/pg-test.sh` uses those
binaries to run a disposable cluster with no Docker and no sudo. The integration suite runs the real
migration chain against it, so the handler-test requirement in `docs/TESTING.md` §2 is genuinely met
rather than waived. See ADR-036 for why the fixture never skips.

The next real toolchain blocker is **Windows**, at Stage 09.

### 4.4 CI workflow is written but not pushed — needs a token scope

`.github/workflows/ci.yml` exists on disk and is complete, but GitHub **rejected the push**:

```
refusing to allow an OAuth App to create or update workflow
.github/workflows/ci.yml without `workflow` scope
```

The `gh` token holds `repo`, `read:org`, `gist` and `admin:public_key`, but not `workflow`. Granting
it needs an interactive browser flow. To land the file:

```bash
gh auth refresh -s workflow      # interactive — approve in the browser
git add .github/workflows/ci.yml
git commit -m "ci(stage-00): build, test and architecture-test pipeline"
git push
```

Until then **there is no CI**: the build/test/architecture-test gates that `docs/TESTING.md` §6 makes
a precondition for marking a stage DONE only run locally.

Stage 01 extended the workflow while it was still unpushed — the `test` job now runs against a
PostgreSQL service container, and `migrate-check` stopped being a no-op and now applies every
migration to an empty database, reverses it and re-applies it. Both sequences were verified locally
against the throwaway cluster, so the file is expected to work on its first run; it just has not had
one. Stage 01 is marked DONE on a verified local run, as Stage 00 was, and this remains the one
outstanding gate for both.

### 4.5 GitHub remote name

The remote is `github.com/Kobershan/zentih-retail` — the repository name contains a typo
("zentih"). The repo was empty at connection time, so `gh repo rename zenith-retail` is safe
whenever the owner wants it; the remote URL then needs updating. Cosmetic, not blocking.

---

## 5. Next session starts here

**Stage 02 — Identity, RBAC, permission catalogue.** Write `docs/stages/STAGE-02-identity.md` first,
using `STAGE-04b-licensing.md` as the template, then execute it. `docs/SECURITY.md` is Stage 02's to
write as well.

### Running the build on this machine

```bash
export DOTNET_ROOT=$HOME/.dotnet && export PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH
dotnet build -c Release
scripts/test.sh                 # starts a throwaway PostgreSQL, runs everything, tears it down
```

`scripts/test.sh` is the only way to run the full suite — the integration tests need a database and
deliberately fail rather than skip without one (ADR-036).

### What Stage 01 leaves you

- `EntityConfiguration<T>` maps every §7 rule 3 column. **Derive from it and describe only your own
  columns.** If you find yourself re-declaring a base column, the base is wrong, not your entity.
- Both global query filters are automatic. Do not add `Where(x => !x.IsDeleted)` or a tenant
  predicate to a query — they are already there, and a second one hides a bug in the first.
- `AuditInterceptor` stamps audit fields, rewrites `Remove` into a soft delete, guards
  `IImmutableRecord`, replaces `row_version` and writes the audit trail. **Never set an audit field
  in business code.**
- `Schemas` holds the schema-name constants; add `identity` usage there. An architecture test fails
  the build if a table lands outside a declared schema, or if a foreign key crosses one.
- Every new entity needs `[Replicated(scope, conflictPolicy)]` or the build fails (ADR-007).
  `NodeLocal` is a valid answer for a device-local table; silence is not.
- Take `IClock` — `DateTimeOffset.UtcNow` anywhere in `src/` fails the build.
- Add the new tables to `BaseEntityMappingTests.AllTables` so they get the `information_schema`
  conformance sweep for free.

### What Stage 02 owes the rest of the build

1. ASP.NET Core Identity over the existing `ZenithRetailDbContext`, in the `identity` schema, with
   users, roles and the `module.entity.action` permission catalogue (ADR-013).
2. **A real `IPrincipalAccessor`**, backed by the authenticated identity and carrying the terminal id.
   Register it before `AddZenithPersistence` — that registration is try-add precisely so this is a
   registration rather than an edit to Stage 01's code. R6's audit trail is currently recording
   `system:host` for everything.
3. Setting `ITenantContext` at the request edge from the authenticated principal. Nothing does this
   yet, which is why every test constructs it by hand.
4. JWT issuance (15 min access / 30 day rotating refresh), device certificates for terminals, and the
   4–8 digit POS operator PIN (`CLAUDE.md` §4).
5. `docs/SECURITY.md`, including the POPIA handling `CLAUDE.md` §9 points at.

One thing to watch: `Terminal` will want a foreign key to `platform.stores`, and **it cannot have
one** — that is a cross-schema foreign key and the architecture test will fail the build. Hold the
store id as a plain `uuid` and resolve through the platform module's contract (ADR-010).

### CI has never run

`.github/workflows/ci.yml` still has not been pushed (§4.4), so neither Stage 00 nor Stage 01 has
been verified by CI — both are green on a local run only. The workflow now includes a PostgreSQL
service container for the test job and a real `migrate-check` (up → down → up), so its first run will
exercise considerably more than it would have. **Land it before Stage 02 is marked DONE.**
