# STAGE 00 — Foundation: solution skeleton, conventions, CI

**Status:** DONE (2026-08-09) · **Depends on:** — · **Reference reading:** `CLAUDE.md` §4, §5, §7, §8, `docs/DECISIONS.md` ADR-001, ADR-004, ADR-009, ADR-010, `docs/CONVENTIONS.md`

## Objective

The skeleton every later stage builds inside: a solution whose **project references make the wrong
design a compile error**, the base entity that gives every table its mandatory columns, the CQRS
abstractions with the side-effect classification that read-only enforcement later depends on, and a
CI pipeline that fails on the things `CLAUDE.md` §7 says must never happen.

This stage writes almost no business logic on purpose. Its value is that Stage 01 onward cannot
drift: layering is enforced by references and by tests, not by discipline.

Two decisions are made here and recorded as ADRs: central package management, and deferring the
Windows-only projects to the stages that need them.

## Deliverables

**Build configuration**
- `VumaRetail.sln` containing every project below.
- `global.json` pinning the SDK band (`9.0.100`, `rollForward: latestFeature`) so CI and every
  developer resolve the same compiler.
- `Directory.Build.props` — `net9.0`, C# 13, `ImplicitUsings`, `Nullable` enabled solution-wide,
  deterministic builds. **`TreatWarningsAsErrors` on `Domain` and `Application` only** (`CLAUDE.md`
  §7 rule 10) — host projects warn but do not break.
- `Directory.Packages.props` — central package management, so one version of every dependency
  across ~15 projects.
- `.editorconfig` — formatting plus the analyzer severities that back the conventions.

**Project skeleton** (`src/`) — each with a single `AssemblyMarker` type so the architecture tests
have something to bind to:

| Project | TFM | References |
|---|---|---|
| `VumaRetail.Domain` | net9.0 | **nothing** |
| `VumaRetail.Application` | net9.0 | Domain |
| `VumaRetail.Contracts` | net9.0 | nothing |
| `VumaRetail.Infrastructure` | net9.0 | Application, Domain, Contracts |
| `VumaRetail.Sync` | net9.0 | Application, Domain, Contracts |
| `VumaRetail.StoreServer` | net9.0 | Infrastructure, Sync, Contracts |
| `VumaRetail.CloudApi` | net9.0 | Infrastructure, Sync, Contracts |
| `VumaRetail.PublicApi` | net9.0 | **its own DTOs only** — not Contracts (§7 rule 14) |

**Domain primitives**
- `Entity` — the mandatory columns from §7 rule 3: `Id`, `TenantId`, `StoreId?`, `CreatedAt/By`,
  `UpdatedAt/By`, `RowVersion`, `SyncState`, plus `DeletedAt/By` for rule 8. Every table inherits it.
- `UuidV7` — RFC 9562 time-ordered ID generation, client-side (ADR-004).
- `Money` — `decimal(18,4)` + mandatory ISO currency code (§7 rule 4). Arithmetic across two
  currencies throws; there is no implicit conversion.
- `Quantity` — `decimal(18,6)` + unit-of-measure reference (§7 rule 5).
- `SyncState`, `ConflictPolicy` enums (ADR-007) — declared here so entities can be classified from
  Stage 01 onward.

**Application abstractions** (ADR-009 — hand-rolled CQRS, not MediatR)
- `ICommand<TResult>`, `IQuery<TResult>`, `ICommandHandler<,>`, `IQueryHandler<,>`.
- `SideEffect` classification: **every command declares `Write` or `ReadOnly`**. This is the hook
  Stage 04b's read-only interceptor uses, and an unclassified command must fail the build — see
  `STAGE-04b-licensing.md` and ADR-028. Building the mechanism now costs nothing; retrofitting it
  across forty modules later costs a great deal.

**Tests** (`tests/`)
- `VumaRetail.ArchitectureTests` — NetArchTest, enforcing:
  - Domain depends on nothing outside the BCL (§7 rule 1)
  - Application depends only on Domain
  - `PublicApi` does not reference `Contracts` (§7 rule 14)
  - every `ICommand` implementation carries a `SideEffect` classification (§7 rule 15)
- `VumaRetail.UnitTests` — xUnit + FluentAssertions covering the domain primitives: UUID v7
  ordering and version/variant bits, `Money` currency mismatch and precision, `Quantity` precision.

**CI** — `.github/workflows/ci.yml`: `build` → `test` → `architecture-tests`, on push and PR.
`migrate-check` and `package` are declared as jobs but no-op until Stage 01 introduces migrations
and Stage 31 introduces packaging.

**Docs** — `docs/CONVENTIONS.md`: naming, file layout, async suffixes, nullability, the
`module.entity.action` permission string shape (ADR-013), commit message format, and the rule that
schema names match module names (ADR-010).

## Business rules

None — this stage has no business behaviour. The rules it encodes are `CLAUDE.md` §7 rules 1, 3, 4,
5, 8, 10, 14 and 15, and it encodes them as compile errors and failing tests rather than prose.

## Tests / acceptance

- `dotnet build -c Release` — zero warnings in Domain and Application, zero errors anywhere.
- `dotnet test` — all green.
- Adding a project reference from `Domain` to `Application` breaks the build. Verify by trying it.
- A command without a `SideEffect` classification fails the architecture test. Verify with a
  deliberate violation, then remove it.
- `Money` rejects arithmetic across two currency codes.
- UUID v7 values generated in sequence sort in generation order, and carry version 7 / variant 2.

## Exit checklist

- [x] Solution builds Release with zero warnings in Domain/Application — `0 Warning(s) 0 Error(s)`
- [x] All tests green — 55 passed (46 unit, 9 architecture), 0 failed
- [x] Architecture tests actually fail when the rule is violated — **proven**, see below
- [x] `Directory.Packages.props` is the single source of package versions; no `Version=` on any
      `PackageReference`
- [~] CI workflow written and complete, but **not pushed** — the token lacks the `workflow` scope.
      See `docs/PROGRESS.md` §4.4 for the one command that fixes it
- [x] `docs/CONVENTIONS.md` written
- [x] ADRs appended — ADR-030 to ADR-034
- [x] `docs/PROGRESS.md` updated with the handoff into Stage 01
- [ ] CI observed green on GitHub Actions — *first run happens on the push that lands this stage*

### Enforcement proven, not assumed

Both mechanisms were broken on purpose and the break was confirmed before being reverted:

1. **Unclassified command.** Adding an `ICommand<Unit>` with no `[CommandSideEffect]` to
   `Infrastructure` failed `Every_command_declares_a_side_effect` with the offending type named:
   `VumaRetail.Infrastructure.DeliberatelyUnclassifiedCommand — no [CommandSideEffect] attribute`.
2. **Wrong-direction reference.** Adding a `Domain → Application` project reference failed the build
   outright (`MSB4006: circular dependency`), before any test ran. Layering is a compile error, as
   §7 rule 1 intends; the NetArchTest rules then cover the acyclic cases a reference cannot catch,
   such as a framework arriving transitively.

### What this stage did not verify

Zero-coverage measurement: the ≥ 80% Domain/Application line-coverage bar is collected in CI but was
not asserted locally. It is not meaningful yet — the code under test is the primitives themselves,
which are covered, and there is no business logic. Stage 01 is the first stage where the number
means something and where the gate should start blocking.

## Explicitly deferred

`VumaRetail.Desktop` (WPF), `VumaRetail.Hardware` and `VumaRetail.UiTests` (FlaUI) target
`net9.0-windows` and **cannot build on Linux at all**. They are created by the stages that need them
(09 and 30 respectively), on a Windows machine. `VumaRetail.Imports`, `.Reporting` and
`.ControlPlane` are created by Stages 11, and 30b. Creating empty shells now would only add
projects that no test exercises. See ADR-031.
