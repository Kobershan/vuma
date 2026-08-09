# PROGRESS — Zenith Retail

> ★ **THE STATE FILE.** Read first, write last. This file is the truth about where the build is;
> `ROADMAP.md` is only the plan. If they disagree, correct the roadmap.

**Last updated:** 2026-08-09 · **Current stage:** 00 — Foundation · **Status:** IN_PROGRESS

---

## 1. Stage status

| Stage | Title | Status | Changed |
|---|---|---|---|
| 00 | Foundation — solution skeleton, conventions, CI | IN_PROGRESS | 2026-08-09 |
| 01 – 31 | see `ROADMAP.md` | NOT_STARTED | — |

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
| .NET 9 SDK | **not installed** | `dotnet build` / `dotnet test` cannot run; §8 Definition of Done cannot be verified locally |
| Docker | **not installed** | Testcontainers integration tests cannot run |
| Windows | n/a — Linux | `ZenithRetail.Desktop` (WPF, `net9.0-windows`) and FlaUI UI tests cannot build or run here at all |

The cross-platform projects (`Domain`, `Application`, `Contracts`, `Infrastructure`, and the
ASP.NET Core hosts) will build anywhere once the SDK is installed. Any stage touching the desktop
shell needs a Windows machine or VM. Until the SDK is present, `stage-verifier` must report build
and test boxes as `UNVERIFIED`, never as passing.

### 4.4 GitHub remote name

The remote is `github.com/Kobershan/zentih-retail` — the repository name contains a typo
("zentih"). The repo was empty at connection time, so `gh repo rename zenith-retail` is safe
whenever the owner wants it; the remote URL then needs updating. Cosmetic, not blocking.

---

## 5. Next session starts here

Stage 00 is in progress. Write `docs/stages/STAGE-00-foundation.md` if absent, then complete the
foundation: solution file, the cross-platform project skeleton with layering enforced by project
references, `Directory.Build.props` (nullable, warnings-as-errors on Domain/Application, C# 13),
`.editorconfig`, `docs/CONVENTIONS.md`, the GitHub Actions workflow
(`build`/`test`/`migrate-check`/`package`), and the architecture-test project that enforces
`CLAUDE.md` §7 rule 1.

**Install the .NET 9 SDK first.** Without it the stage cannot pass its own exit checklist, and a
stage that cannot be verified must not be marked DONE.
