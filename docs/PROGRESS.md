# PROGRESS — Zenith Retail

> ★ **THE STATE FILE.** Read first, write last. This file is the truth about where the build is;
> `ROADMAP.md` is only the plan. If they disagree, correct the roadmap.

**Last updated:** 2026-08-09 · **Current stage:** 01 — Persistence core · **Status:** NOT_STARTED

---

## 1. Stage status

| Stage | Title | Status | Changed |
|---|---|---|---|
| 00 | Foundation — solution skeleton, conventions, CI | **DONE** | 2026-08-09 |
| 01 | Persistence core — EF Core, base entity mapping, migrations, audit | NOT_STARTED | — |
| 02 – 31 | see `ROADMAP.md` | NOT_STARTED | — |

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

---

## 3. Deferred — needs real credentials

Nothing yet. Stage 04b (licence signing key / KMS) and Stage 30b (payment gateway) will be the
first entries.

---

## 4. Blockers and known gaps

### 4.1 Documents referenced by `CLAUDE.md` §5 that do not exist yet

These are cited as reference reading by stages that will need them. Each should be written by the
stage that first depends on it, not all up front:

`ARCHITECTURE.md` · `DATA_MODEL.md` · `API_STANDARDS.md` (Stage 03) · `SYNC_AND_BACKUP.md`
(Stage 04) · `IMPORT_PIPELINE.md` (Stage 11) · `SECURITY.md` (Stage 02) · `CONVENTIONS.md`
(Stage 00) · `HARDWARE.md` (Stage 09) · `API_LOYALTY.md` (Stage 20) · `API_ECOMMERCE.md`
(Stage 21) · `GAP_ANALYSIS.md`.

Stage documents exist only for **04b** and **30b**. Every other stage document must be written by
the session that executes it, using `STAGE-04b-licensing.md` as the template.

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
| Docker | **not installed** | Testcontainers integration tests cannot run. **Blocks Stage 01's exit checklist**, which requires handler tests against real PostgreSQL and a reversible migration |
| Windows | n/a — Linux | `ZenithRetail.Desktop` (WPF), `.Hardware` and the FlaUI UI tests cannot build or run here at all. Blocks Stage 09 onward (ADR-031) |

Docker is the next thing to install — Stage 01 introduces EF Core and migrations, and
`docs/TESTING.md` §2 requires handler integration tests against a real database rather than a mocked
`DbContext`. A stage whose tests cannot run must not be marked DONE, so `stage-verifier` reports those
boxes as `UNVERIFIED`, never as passing.

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
a precondition for marking a stage DONE only run locally. Stage 01 should not be marked DONE on a
green local run alone once CI is meant to exist.

### 4.5 GitHub remote name

The remote is `github.com/Kobershan/zentih-retail` — the repository name contains a typo
("zentih"). The repo was empty at connection time, so `gh repo rename zenith-retail` is safe
whenever the owner wants it; the remote URL then needs updating. Cosmetic, not blocking.

---

## 5. Next session starts here

**Stage 01 — Persistence core.** Write `docs/stages/STAGE-01-persistence.md` first, using
`STAGE-04b-licensing.md` as the template, then execute it.

Before anything else: **install Docker** — Stage 01's exit checklist needs Testcontainers, and without
it the stage cannot be verified and so cannot be marked DONE. (`dotnet` is already on `PATH`.)

What Stage 01 owes the rest of the build:

1. `ZenithRetailDbContext` in Infrastructure, with the `Entity` base columns mapped **once** in a base
   configuration rather than per entity — `RowVersion` to PostgreSQL `xmin`, all timestamps
   `timestamptz`, `SyncState` and `ConflictPolicy` as enums.
2. A global query filter for soft delete (§7 rule 8), and the tenant filter, so no module has to
   remember either.
3. `SaveChanges` interceptors that stamp `MarkCreated`/`MarkUpdated` from the ambient principal and
   clock. Business code must never set an audit field itself.
4. `IClock` — no `DateTime.Now` anywhere in the solution, so tests can control time.
5. The first EF migration, schema-per-module naming per ADR-010 and `docs/CONVENTIONS.md` §2, with
   `Down` tested to actually reverse.
6. `docs/DATA_MODEL.md`, and turn on the `migrate-check` CI job that currently no-ops.

Two architecture rules should be added to `ZenithRetail.ArchitectureTests` while the persistence layer
is being built, because both become unenforceable once modules exist: **no `SaveChanges` outside the
command pipeline** (§7 rule 2) and **no cross-schema foreign keys** (`CONVENTIONS.md` §2).
