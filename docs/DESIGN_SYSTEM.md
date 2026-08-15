# DESIGN SYSTEM — Vuma Retail

One system, four surfaces: the Windows desktop app (POS + back office), the Android admin app, the
supplier/retailer web portal, and the customer storefront. Built in Stage 08b, consumed by every UI
stage after it.

## 1. Direction

Apple-inspired, in the ways that actually matter: **generous space, one accent, type doing the work,
depth from material rather than decoration, and motion that explains rather than entertains.** Not a
skeuomorphic imitation — a discipline.

Grounded in the subject. *Vuma* means "agree" — the moment a deal is struck, a sale rings up, a
supplier's price is accepted. The system's signature is that moment of confirmation: **the Vuma
tick**, a single stroke that draws itself in 220ms on any successful commit — a completed sale, an
accepted price list, a posted journal, a delivered order. It is the one flourish in the product, and it
appears nowhere else.

Retail reality shapes everything else. Screens get used in direct sunlight through a shopfront window,
with gloved hands in a cold room, on a filthy touchscreen at a till, by someone who has been standing
for six hours. So: high contrast, big targets, no thin type on the operational screens, and the
important number always the largest thing on the screen.

## 2. Colour

Two themes, both first-class. Dark is the default for POS (a bright screen facing a customer at night
is unpleasant); light is the default for back office and portal.

### Light
| Token | Hex | Use |
|---|---|---|
| `surface/base` | `#FAFAFA` | app background |
| `surface/raised` | `#FFFFFF` | cards, sheets, till lines |
| `surface/sunken` | `#F0F0F2` | wells, input backgrounds |
| `separator` | `#E3E3E6` | hairlines |
| `text/primary` | `#0A0A0B` | headings, amounts |
| `text/secondary` | `#5A5A62` | labels, metadata |
| `text/tertiary` | `#8E8E96` | placeholders, disabled |

### Dark
| Token | Hex | Use |
|---|---|---|
| `surface/base` | `#000000` | app background — true black, not charcoal |
| `surface/raised` | `#141416` | cards, sheets |
| `surface/sunken` | `#0A0A0B` | wells |
| `separator` | `#2A2A2E` | hairlines |
| `text/primary` | `#F5F5F7` | headings, amounts |
| `text/secondary` | `#A0A0A8` | labels |
| `text/tertiary` | `#6A6A72` | placeholders, disabled |

### Accent and semantics (identical in both themes unless noted)
| Token | Light | Dark | Use |
|---|---|---|---|
| `accent` | `#0B7A5A` | `#16A97D` | primary actions, the Vuma tick, selection |
| `accent/quiet` | `#E6F3EE` | `#0E2A22` | accent-tinted backgrounds |
| `positive` | `#0B7A5A` | `#16A97D` | payment received, in stock, on time |
| `warning` | `#B26A00` | `#E5940A` | low stock, expiring, approaching limit |
| `critical` | `#C0261F` | `#FF5A4E` | failures, out of stock, overdue |
| `info` | `#1B5FB0` | `#4C94F0` | neutral system messages |

A deep green accent, not the default AI-blue or a warm terracotta. It reads as "confirmed" and it holds
up against the greys, the browns and the printed reds of an actual shop floor.

**Never encode meaning in colour alone** — every state carries an icon or a word too. Some of your
users are colour-blind, and some screens are five years old with a yellow cast.

## 3. Type

| Role | Face | Notes |
|---|---|---|
| Display / numerals | **Inter Display** (or Inter with `Display` optical size) | tabular figures **always on** for money and quantities — columns must align |
| Body / UI | **Inter** | |
| Mono / codes | **JetBrains Mono** | SKUs, barcodes, licence keys, references |

Inter is used deliberately, not by default: it has true tabular figures, excellent small-size legibility
on the low-DPI panels retailers actually buy, and a full Latin Extended range for South African
languages. All three are open-licensed, so nothing blocks a shipped installer.

### Scale
| Token | Size / line | Weight | Use |
|---|---|---|---|
| `display` | 44 / 48 | 600 | the till total, the dashboard headline number |
| `title1` | 30 / 36 | 600 | page titles |
| `title2` | 22 / 28 | 600 | section headers |
| `headline` | 17 / 22 | 600 | card titles, emphasised rows |
| `body` | 15 / 21 | 400 | default |
| `callout` | 14 / 19 | 400 | secondary content |
| `caption` | 12 / 16 | 500 | labels, table headers, metadata |
| `mono` | 13 / 18 | 400 | codes and references |

**On POS screens nothing goes below `callout`, and no weight below 400.** A cashier is not reading a
dashboard at leisure.

## 4. Space, shape, depth

- **4pt base grid.** Spacing tokens: 4, 8, 12, 16, 24, 32, 48, 64.
- **Radius:** `sm` 8, `md` 12, `lg` 16, `xl` 22, `pill` 999. Cards use `lg`, buttons `md`, sheets `xl`.
- **Depth is material, not shadow.** Layer by surface tone first; use shadow only for things that
  genuinely float (menus, sheets, dialogs, drag). Two elevations, no more:
  `e1` `0 1px 2px rgba(0,0,0,.06), 0 1px 1px rgba(0,0,0,.04)` — cards
  `e2` `0 8px 28px rgba(0,0,0,.12)` — overlays. In dark theme, halve the opacity and add a `separator`
  hairline instead; shadows barely read on true black.
- **Hairlines are 1 physical pixel**, not 1 logical unit. On a 4K till screen a 1dp line looks like a
  border.

## 5. Touch and input

| Context | Minimum target | Notes |
|---|---|---|
| POS primary (tender, quick keys) | 64 × 64 | thumb-sized, spaced 8pt apart |
| POS secondary | 48 × 48 | |
| Back office | 36 × 36 | mouse-driven |
| Android warehouse / driver | 56 × 56 | one-handed, gloved, moving |

**Every action has a keyboard path.** Cashiers are faster on keys than on glass, and a broken
touchscreen must not stop trade. Focus is always visible: a 2pt `accent` ring at 2pt offset — never
removed, never `outline: none`.

## 6. Motion

| Token | Duration | Curve | Use |
|---|---|---|---|
| `instant` | 100ms | ease-out | state flips, checkbox, toggle |
| `quick` | 180ms | `cubic-bezier(.2,0,0,1)` | hover, focus, small reveals |
| `standard` | 260ms | `cubic-bezier(.2,0,0,1)` | sheets, navigation, expansion |
| `vuma-tick` | 220ms | `cubic-bezier(.65,0,.35,1)` | the confirmation stroke |

Motion explains where something came from and where it went. Nothing decorative, nothing looping,
nothing on a data table. **`prefers-reduced-motion` is honoured everywhere** — the tick becomes an
instant state change, not a slower animation.

## 7. Components

Specified once in Stage 08b, reused everywhere: button (primary/secondary/quiet/destructive), input,
select, combo with type-ahead, numeric stepper, money field (tabular, currency-aware), quantity field,
date/range, search, toggle, segmented control, checkbox, radio, card, list row, data table (sticky
header, virtualised, column resize, sortable), tabs, sheet, dialog, toast, banner, empty state, skeleton
loader, progress, stat tile, sparkline, chart set, avatar, badge, chip, pagination, breadcrumb, side
nav, command palette, keypad, tender pad, receipt preview, scanner input, offline indicator,
licence-state banner.

**Every component ships in both themes, both densities (comfortable / compact), and with a keyboard
spec and an accessibility spec.** A component without those is not done.

### Two components carry the product
- **Till line list** — dense, tabular, instantly scannable, with the running total pinned and typeset in
  `display`. The single most-looked-at surface in the entire system.
- **Stat tile** — one number, one label, one comparison, one sparkline. Never more. It is what the owner
  looks at on their phone at 6am, and it must answer the question in under a second.

## 8. Theming and platform

- Tokens live in one source of truth (`design/tokens.json`) and are **generated** into a WPF
  `ResourceDictionary`, an Android Compose theme, and CSS custom properties. Hand-maintained parallel
  palettes drift within a month — generate them.
- Theme follows the OS by default with a manual override per user, and **per-terminal** override so a
  dark-mounted till and a bright back office can differ in the same store.
- Tenant branding is limited to logo, accent colour and receipt header. Tenants cannot change spacing,
  type or semantics — that is what keeps every Vuma install recognisable and supportable.
- Contrast: WCAG AA minimum everywhere, **AAA for money, quantities and any critical state**. Verified
  in CI against the token set, so a palette tweak cannot silently break it.

## 9. Voice

Plain, active, specific. Buttons say what happens: **Take payment**, not *Submit*. The same word all the
way through a flow — a button that says *Publish* produces a toast that says *Published*.

Errors say what happened and what to do, in the interface's voice, and never apologise:
*"Card declined — code 51, insufficient funds. Try another tender or ask the customer to call their
bank."* Not *"Sorry, something went wrong."*

Empty states are an invitation: *"No stock counted yet. Scan an item to start."*

Money always shows its currency and always uses tabular figures. Quantities always show their unit.
Dates are unambiguous (`2026-08-11`, or `Tue 11 Aug`), never `08/11`.
