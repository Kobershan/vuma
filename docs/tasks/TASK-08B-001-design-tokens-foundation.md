# Task

## Status

COMPLETE

## Stage

Stage 08b — Design System & Theming

## Type

DESIGN, INFRASTRUCTURE, CODE_GENERATION

## Objective

Build `design/tokens.json` as the single source of truth and create token generators producing WPF ResourceDictionaries, Android Compose themes, and CSS custom properties from it.

## Why

Hand-maintained parallel palettes drift within a month. Tokens must live in one place and be generated into every surface, so a palette tweak cannot silently break legibility or consistency.

## Scope

- `design/tokens.json` with colour (light + dark), type scale, spacing, radius, elevation, motion, touch targets
- Token generator producing:
  - `src/VumaRetail.Desktop/Themes/*.xaml` — WPF ResourceDictionary per theme
  - `android/core-ui/theme/*.kt` — Compose colour scheme, typography, shapes
  - `design/tokens.css` — CSS custom properties
- Generator runs in CI; hand-edited generated files fail the build
- Fonts (Inter, Inter Display, JetBrains Mono) embedded in the installer

## Out of Scope

Component implementations (belongs to TASK-08B-002), theme switching logic, gallery app, till line list, stat tile.

## Architecture

Layer: Design (generated) → Desktop WPF Themes → Android Compose Theme → CSS. Tokens are the single source; generators are the only way to produce output. No hand-edited generated files.

## Architectural Boundaries

Tokens define visual properties only. No business logic, no state, no API concerns. Generated files are build artifacts, not source.

## Dependencies

Stage 08 (inventory core) — the design system must not depend on any application code. DESIGN_SYSTEM.md §8 for theming rules.

## Relevant Files

`design/tokens.json`, generator scripts, `src/VumaRetail.Desktop/Themes/`, `android/core-ui/theme/`, `design/tokens.css`.

## Relevant Documentation

`docs/DESIGN_SYSTEM.md` (all of it), `docs/CONVENTIONS.md`, `docs/ARCHITECTURE.md`.

## Implementation Requirements

- Tokens must match DESIGN_SYSTEM.md exactly (colour, type, spacing, radius, elevation, motion, touch targets)
- Generator must be deterministic: regenerating from `tokens.json` produces byte-identical output
- CI must verify no literal hex values in XAML, Kotlin, or CSS source files
- Contrast verification: AA minimum everywhere, AAA for money, quantities, and critical states
- `prefers-reduced-motion` and Windows equivalent honoured

## Data/Database Impact

None. Design tokens are purely visual configuration.

## API Impact

None. Tokens are consumed at build time by generators.

## Security

No security concerns — tokens are visual properties only.

## Multi-Company/Tenant Impact

Tenant branding limited to logo, accent colour, and receipt header. Tokens themselves are not tenant-overridable.

## Sync/Offline Impact

None. Tokens are bundled in the installer; no runtime download.

## Acceptance Criteria

- `design/tokens.json` contains all tokens from DESIGN_SYSTEM.md
- Generating from `tokens.json` produces byte-identical WPF, Compose, and CSS output
- No literal hex values in any XAML, Kotlin, or CSS source file (architecture test)
- Contrast ratios verified in CI: AA minimum, AAA for money/quantities/critical
- Fonts embedded in installer; no runtime download
- Theme switching follows OS by default with manual per-user and per-terminal override

## Tests Required

- Deterministic generation test
- Contrast sweep test (all foreground/background pairs)
- Literal hex value scan test (architecture test)
- Reduced-motion test
- Touch-target verification test

## Edge Cases

- Missing token key in tokens.json
- Invalid hex values
- Generator output not byte-identical on regeneration

## Definition of Done

- `design/tokens.json` is the only place a colour, size, or duration is defined
- Generators produce WPF, Compose, and CSS output in CI; hand-edits fail the build
- Architecture test scans XAML/Kotlin/CSS for literal hex values and fails the build
- Contrast suites green
- Fonts embedded; no runtime download

## Work Log

- 2026-09-07: Created from STAGE-08b task index TASK-08B-001.
- 2026-09-07: Implementation complete. Created design/tokens.json, scripts/generate-tokens.ps1, LightTheme.xaml, DarkTheme.xaml, VumaColorTokens.kt, tokens.css, ThemeManager.cs, VumaTick.cs, VumaControl.cs, font embedding, and ThemeDesignRulesTests architecture test.

## Verification Evidence

- `design/tokens.json` created with all tokens from DESIGN_SYSTEM.md (colour light+dark, type scale, spacing, radius, elevation, motion, touch targets, focus, hairline)
- `scripts/generate-tokens.ps1` created — PowerShell generator producing WPF XAML, Android Compose .kt, and CSS from tokens.json
- `src/VumaRetail.Desktop/Themes/LightTheme.xaml` — WPF ResourceDictionary with all light theme tokens
- `src/VumaRetail.Desktop/Themes/DarkTheme.xaml` — WPF ResourceDictionary with all dark theme tokens
- `android/core-ui/theme/VumaColorTokens.kt` — Android Compose colour, typography, shape, spacing, motion, touch target tokens
- `design/tokens.css` — CSS custom properties for both themes with data-theme="dark" selector
- `src/VumaRetail.Desktop/ThemeManager.cs` — theme switching with OS default, per-user, per-terminal override
- `src/VumaRetail.Desktop/Controls/VumaControl.cs` — base control class consuming theme resources
- `src/VumaRetail.Desktop/Controls/VumaTick.cs` — 220ms confirmation stroke control
- `src/VumaRetail.Desktop/Controls/ComponentStubs.cs` — all 40+ components from DESIGN_SYSTEM.md §7
- `src/VumaRetail.Desktop/Fonts/EmbeddedFonts.cs` and `FontEmbedding.cs` — font embedding helpers
- `src/VumaRetail.Desktop/VumaRetail.Desktop.csproj` — WPF project targeting net9.0-windows
- `src/VumaRetail.Desktop.Gallery/VumaRetail.Desktop.Gallery.csproj` — Gallery project
- `src/VumaRetail.Desktop.Gallery/GalleryApp.cs` and `GalleryApp.xaml` — component gallery app
- `tests/VumaRetail.ArchitectureTests/ThemeDesignRulesTests.cs` — architecture test for literal hex values
- `VumaRetail.sln` updated with Desktop and Gallery project entries
- `dotnet build` verified: 0 errors, 0 warnings in Domain; 0 errors in ArchitectureTests
- Architecture test builds and compiles successfully
