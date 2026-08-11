# TESTING — Vuma Retail

No stage is DONE without tests. "It compiles" is not evidence.

## 1. The pyramid

| Level | Tool | Scope | Target |
|---|---|---|---|
| Unit | xUnit + FluentAssertions + NSubstitute | domain rules, calculations, state machines | ≥ 80% line coverage on Domain + Application |
| Integration | xUnit + Testcontainers (Postgres) | handlers against a real DB, migrations, EF mappings | every command + query handler |
| API | `WebApplicationFactory` | routing, auth, validation, ProblemDetails, OpenAPI | every endpoint, happy + 401 + 403 + 422 + 409 |
| Sync | custom harness, 3 simulated nodes | offline → reconnect → convergence, conflicts | every replicated entity |
| UI | FlaUI | POS critical paths only | sale, refund, cash-up, offline sale |
| Load | NBomber | POS throughput, sync batch size | see §4 |

## 2. Mandatory test classes per stage

Every stage adds, at minimum:
1. **Domain invariant tests** — the rules that must never break (e.g. "a posted invoice cannot be edited").
2. **Handler integration tests** — real Postgres via Testcontainers, real migrations, no mocked DB.
3. **Permission tests** — a user without the permission gets 403; a user in another store gets 404.
4. **Sync test** — create the entity on a disconnected node, reconnect, assert convergence on all tiers.
5. **Offline test** (if the POS touches it) — the operation queues and replays correctly.

## 3. Money and quantity testing

Financial calculation bugs are the most expensive kind here. Every pricing, tax, discount and
valuation change needs table-driven tests including:
- VAT inclusive **and** exclusive, zero-rated and exempt
- rounding at 0.005 boundaries, and total-vs-line rounding reconciliation (the invoice total must
  equal the sum of lines after rounding — assert it)
- multi-currency with an fx rate that is not 1
- stacked promotions with priority and the `stackable` flag
- negative quantities (returns) and their tax/cost reversal
- weighted-average cost after receipt-issue-receipt sequences
- a full stock ledger → balance projection rebuild that must equal the incremental projection

## 4. Performance budgets (asserted, not aspirational)

| Operation | Budget |
|---|---|
| Barcode scan → line on screen | < 150 ms |
| Sale completion (10 lines, cash) | < 500 ms end-to-end |
| Receipt print dispatch | < 1 s |
| Offline sale write to SQLite | < 100 ms |
| Item quick-search (100k items) | < 200 ms |
| Store dashboard load | < 2 s |
| Sync batch of 2000 ops | < 30 s |
| Nightly full backup (10 GB) | < 20 min |
| Restore drill (10 GB) | < 2 h |

Load profile to sustain: 20 terminals per store, 10 sales/minute/terminal at peak, 250k items,
1M customers, 5 years of ledger history.

## 5. Test data

`scripts/seed.ps1` builds a deterministic demo tenant: 2 stores, 3 warehouses, 5000 items with
variants and barcodes, 200 suppliers, 10000 customers, 12 months of sales history, live promotions,
open POs, a work order, an open service ticket, a published roster. Every stage extends the seed so
the whole system stays demonstrable — this doubles as the DR-drill dataset.

Use Bogus for generated data, but keep **fixed seeds** so failures are reproducible.

## 6. CI gates

`build` → `test` → `migrate-check` (migrations apply cleanly to an empty DB *and* to the previous
release's DB, and `Down` reverses) → `architecture-tests` → `vulnerability-scan` → `package`.
A red pipeline blocks the stage from being marked DONE.

---

## 7. Licensing test requirements (added in revision 2)

Two suites are mandatory and must be run in CI on every build, not just during Stage 04b:

**No-accidental-lockout.** This is the suite that protects the business from its own licensing, and it
must be green on every build (the guarantees survive from ADR-027 into the live ADR-028, restated
against **read-only** rather than a hard lock — see `docs/LICENSING.md` §4):
- Control plane unreachable for the full tolerance window: the store trades normally throughout, and
  drops to read-only only at the configured boundary — **never earlier**
- Control plane returning 500s, timeouts, or garbage: treated as unreachable, never as unlicensed
- A single failed charge, and a second, and a third: **no read-only restriction** until dunning
  completes and notifications are recorded as delivered
- Clock moved backwards, forwards, or into next year: reported as a tamper flag, never a restriction and
  never an extension
- Hardware fingerprint change within tolerance: no restriction
- Payment received while read-only: full write access restored at the next heartbeat, under 60 seconds,
  with no manual step
- Emergency write code: restores full write access **with no internet at all**, expires exactly on
  time, cannot be reused, and cannot be forged without the signing key
- Write unlock: a vendor-granted, time-limited restoration of write access to a read-only tenant; read
  access never needed unlocking because it was never blocked
- Open-session carve-out: an in-progress sale and its cash-up complete; no new sale can start

**Read-only correctness.** The other mandatory suite (ADR-028). In read-only, assert that:
- every report, dashboard, export and reprint succeeds — sweep the full report catalogue, not a sample
- every write is refused with `403 LICENCE_READ_ONLY`: sale, stock movement, order, import, approval,
  config change, user change, price change — one test per module, generated from the module manifests
  so a new module cannot be forgotten
- the payment and card-update screens still work, and a payment made through them restores full access
  within 60 seconds
- offline sales already queued on a terminal still flush to the store server and cloud
- backup runs on schedule and restore is permitted
- the public storefront and loyalty APIs still serve reads, and their writes return a **neutral**
  `503` that does not disclose the tenant's billing status to the tenant's own customers

**Metering privacy.** Assert that the serialised metering payload matches a strict whitelist schema,
contains no field sourced from a business table, and contains no free text, personal name, address,
document content or product-level detail. Run it against a fully seeded tenant so the assertion is
meaningful.

Add to the performance budgets in §4:

| Operation | Budget |
|---|---|
| Licence lease verification on startup | < 50 ms |
| Entitlement check (cached) | < 1 ms |
| Daily metering rollup (250k items, 1M transactions) | < 2 min |
| Heartbeat round trip | < 2 s, and never on the UI thread |
