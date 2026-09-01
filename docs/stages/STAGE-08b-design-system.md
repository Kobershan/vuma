# STAGE 08b — Design System & Theming
## Second-pass architecture and task map

The existing objective, deliverables, business rules, acceptance criteria, and referenced documents in this stage remain authoritative. Use [the architecture map](../ARCHITECTURE.md) to navigate project and boundary rules, then load only the references named above. The stage must be decomposed into canonical task files before implementation begins; do not infer missing requirements.

**Architecture checklist:** WHAT/WHY are defined by this stage's Objective; affected layers and components are the projects named by its Deliverables; data/API/security/multi-company/sync/testing constraints are inherited from the linked authority documents. Any missing answer is **NEEDS ARCHITECTURAL CLARIFICATION**. Existing ADRs listed in the stage header apply; a new ADR is required only for a genuinely new architectural decision. Nothing outside this stage's stated scope may change.

| ID | TYPE | TITLE | DEPENDENCIES | STATUS |
|---|---|---|---|---|
| 08b-MAP-01 | ARCHITECTURE | Stage-specific architecture decomposition and implementation task map | Stage dependencies in header | NOT_STARTED |

This row is a planning gate, not an implementation task. Stage 06c is the first fully canonicalized reference graph; future stage rows must be replaced by independently executable task files before that stage is selected.

**Status:** NOT_STARTED · **Depends on:** 08 · **Reference reading:** `docs/DESIGN_SYSTEM.md` (all of it), `docs/CONVENTIONS.md`

## Task index

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-08B-001 | Build design tokens and theme foundation | Stage 08; Windows/WPF | NOT_STARTED |
| TASK-08B-002 | Complete design-system verification | TASK-08B-001 | NOT_STARTED |

## Objective
Build the design system once, before the first pixel of POS is drawn, so that four surfaces — Windows
desktop, Android, supplier portal, customer storefront — look like one product and stay that way. Every
UI stage after this one consumes tokens and components rather than inventing them.

## Deliverables
- `design/tokens.json` — the single source of truth: colour (light + dark), type scale, spacing, radius,
  elevation, motion, touch targets, exactly as specified in `DESIGN_SYSTEM.md`
- **Token generators** producing, from that one file:
  - `src/VumaRetail.Desktop/Themes/*.xaml` — WPF `ResourceDictionary` per theme
  - `android/core-ui/theme/*.kt` — Compose colour scheme, typography, shapes
  - `design/tokens.css` — CSS custom properties for the portal and storefront
  Generation runs in CI; a hand-edited generated file fails the build. Parallel palettes drift within a
  month, so they must never be hand-maintained.
- **WPF component library** (`src/VumaRetail.Desktop/Controls/`) covering the full list in
  `DESIGN_SYSTEM.md` §7, each in both themes, both densities, with a keyboard spec and an
  accessibility spec
- **The two components that carry the product**, built with disproportionate care: the till line list
  (dense, virtualised, running total pinned in `display` type) and the stat tile
- **The Vuma tick** — the 220ms confirmation stroke, implemented once as a reusable control, wired to
  successful commits only. It appears nowhere else in the product.
- Theme switching: follows OS by default, manual override per user, **per-terminal** override so a
  dark-mounted till and a bright back office differ in the same store. Switching is instant, with no
  restart and no flash of the wrong theme.
- Fonts (Inter, Inter Display, JetBrains Mono) embedded in the installer — no runtime download, since a
  store may have no internet on first run
- Tenant branding: logo, accent colour, receipt header. Nothing else is themeable.
- A **component gallery** app (`src/VumaRetail.Desktop.Gallery/`) showing every component in every
  state, theme and density. It is how a developer checks their work and how you demo the system.

## Business rules
- No screen anywhere in the product defines its own colours, spacing, type sizes or radii. An
  architecture test scans XAML, Kotlin and CSS for literal hex values and fails the build.
- Contrast is verified in CI against the token set: AA minimum everywhere, **AAA for money, quantities
  and critical states**. A palette tweak cannot silently break legibility.
- `prefers-reduced-motion` and the Windows equivalent are honoured — the tick becomes an instant state
  change rather than a slower one.
- POS surfaces never use type below `callout` or weight below 400, and never rely on colour alone to
  carry meaning.
- Every component is keyboard-operable and screen-reader labelled before it is considered done.

## Tests / acceptance
- Token generation is deterministic: regenerating from `tokens.json` produces byte-identical output
- Contrast test sweeps every foreground/background token pair in both themes against its required ratio
- Snapshot tests for every component × theme × density × state (default, hover, focus, active, disabled,
  error, loading, empty)
- Keyboard test: every interactive component reachable, operable and with visible focus, using Tab and
  arrow keys only
- Touch-target test: POS primary controls ≥ 64pt, Android warehouse/driver ≥ 56pt, measured not assumed
- Reduced-motion test: no animation exceeds 100ms when the flag is set
- Theme switch under load: 200-line till list switches theme in under 100ms with no flicker
- Till line list virtualisation: 5,000 lines scroll at 60fps
- Sunlight legibility check documented — screenshots at 30% and 100% brightness, reviewed and signed off

## Exit checklist
- [ ] `design/tokens.json` is the only place a colour, size or duration is defined
- [ ] Generators produce WPF, Compose and CSS output in CI; hand-edits fail the build
- [ ] Component gallery runs and shows every component in every state and both themes
- [ ] Contrast, keyboard, touch-target and reduced-motion suites all green
- [ ] The Vuma tick implemented once, used on commits only
- [ ] Fonts embedded; no runtime download
- [ ] `docs/PROGRESS.md` + ADRs updated, committed
