# STAGE 21b — Vuma Connect: Supplier Network & B2B Trading Ecosystem

**Status:** NOT_STARTED · **Depends on:** 12, 14, 21, 07 · **Reference reading:** `docs/API_CONNECT.md`, `docs/DATA_MODEL.md` (connect), `docs/DECISIONS.md` ADR-056

## Task index

| Task ID | Title | Dependencies | Status |
|---|---|---|---|
| TASK-21B-001 | Implement Vuma Connect relationships and catalogues | Stages 07, 12, 14, 21 | NOT_STARTED |
| TASK-21B-002 | Implement Connect settlement and supplier portal | TASK-21B-001; Stage 08b | NOT_STARTED |

## Objective
Turn Vuma from software each retailer runs alone into a **network they trade across**. A supplier gets
their own login, publishes their catalogue and prices once, and every connected retailer sees it in
their own system. The retailer orders from inside Vuma, pays through Vuma, and the goods receipt writes
itself from the supplier's dispatch note.

This is the stage that changes the business model. Every module before it makes one shop better; this
one makes the product more valuable to each shop as more shops join it.

## The three-sided model

```
  SUPPLIER ORG                    VUMA CONNECT                   RETAILER TENANT
  (its own tenant)                  (the network)                (its own tenant)

  publishes catalogue      ──▶   trading relationships    ──▶    sees supplier catalogue
  publishes price lists    ──▶   price list distribution  ──▶    prices land in Stage 12
  receives orders          ◀──   order routing            ◀──    places a PO from Vuma
  confirms + dispatches    ──▶   ASN / dispatch note      ──▶    GRN pre-populated
  invoices                 ──▶   invoice + 3-way match    ──▶    payable raised
  gets paid                ◀──   settlement              ◀──    pays inside the app
```

**Both sides are Vuma tenants.** A supplier is not a second-class portal user — they are a tenant with
their own catalogue, their own stock, their own orders. A wholesaler can be a supplier to twenty
retailers *and* a retailer buying from ten manufacturers, simultaneously, in one account. Model it that
way from the start or you will rewrite it later.

## Deliverables

### Trading relationships
- **Connection request and acceptance** — either side can initiate; both must accept. A connection
  carries: agreed price list, payment terms, credit limit, delivery lead times, minimum order value,
  delivery days, and the account number each party uses for the other.
- Relationship states: `Invited → Pending → Active → Suspended → Ended`, with history
- **Supplier-issued codes** — a supplier generates connection codes (single-use or multi-use, expiring,
  optionally tied to a price tier or region) and hands them to retailers. The retailer enters the code
  in their own Vuma and the relationship is live in seconds. This is the growth mechanism: a rep hands
  out a code on a shop visit instead of emailing a spreadsheet.
- **Supplier-managed retailer users** — a supplier can issue codes that also grant a named contact at
  the retailer access to the supplier's ordering portal, with the supplier controlling that contact's
  permissions and price tier. The retailer's own admin can always see and revoke these — a supplier can
  never quietly hold access inside a retailer's tenant.
- Territory, tier and channel rules: which retailers see which catalogue, at which prices

### Catalogue and price distribution
- Supplier publishes: items with rich content (reusing the Stage 21 PIM), pack configurations, barcodes,
  images, datasheets, certificates, minimum order quantities, lead times, and availability status
- **Price lists** published per tier, per territory, per retailer, **effective-dated and future-dated**.
  A supplier loads December's prices in October and they activate on the first.
- Publication is versioned and reversible: a supplier who publishes a wrong price can roll it back, and
  every retailer sees the correction with an alert rather than silently.
- **Retailer-side landing** — published prices arrive as a *proposed* cost update, not an automatic one.
  They land in the Stage 11 import preview with a full diff, and the retailer accepts, partially accepts
  or rejects. **A supplier can never silently change a retailer's costs**, and that guarantee is what
  makes retailers willing to connect at all.
- Auto-accept is available per supplier, per category, with a percentage-change threshold above which it
  always requires review
- Promotions and deals: supplier specials, volume breaks, rebate offers, new-line listings pushed with
  their own effective windows
- Catalogue matching: the supplier's SKU mapped to the retailer's item, with fuzzy suggestion on first
  connect and the mapping remembered thereafter

### Ordering
- Retailer places a purchase order from inside Vuma against the connected supplier's live catalogue and
  agreed prices — the PO of Stage 12, with the connection filling in prices, MOQs, lead times and
  delivery days automatically
- Suggested orders from Stage 15 planning flow straight into a Connect order
- Supplier receives it in their own Vuma as a **sales order** (Stage 14) — not an email, not a PDF. One
  order, two views, one status.
- Supplier confirms, part-confirms with substitutions, or rejects with a reason; the retailer sees it
  live over SignalR and on their phone
- **Dispatch note / ASN** — the supplier's pick and pack produces an advance shipping notice with line
  quantities, batches, serials and expiry dates. It pre-populates the retailer's GRN, so receiving is
  scan-and-confirm instead of re-keying. This single feature saves more time than everything else in
  this stage combined.
- Delivery tracking through Stage 24 shared across both tenants
- Claims and returns: short delivery, damages, wrong item — raised by the retailer, visible to the
  supplier, resolved to a credit note on both sides at once

### Payment and settlement
- Retailer pays a supplier invoice **from inside Vuma**: card, EFT with reference, instant EFT, or
  debit order against agreed terms, through the Stage 21 `IPaymentGateway` abstraction
- Payment posts simultaneously to the retailer's AP and the supplier's AR (Stage 07) — no reconciliation
  meeting, no "we paid you last Tuesday"
- Remittance advice generated and delivered automatically
- Settlement, fees and payouts: a configurable network transaction fee, held funds where the model
  requires it, payout schedules and a settlement report for both sides
- Statement reconciliation: the supplier's statement matched against the retailer's payables
  automatically, with only genuine exceptions surfaced
- **Vuma is not a bank.** Money movement goes through a licensed provider behind `IPaymentGateway` and
  `ISettlementProvider`; Vuma orchestrates and records. Regulatory posture is documented in
  `docs/compliance/` before any real funds flow, and the interfaces ship with fakes so the stage is
  fully testable without a licence.

### Discovery and network effects
- Supplier directory: a retailer can find suppliers by category, region, delivery day and minimum order
- Supplier profile: catalogue preview, terms, lead times, coverage, and verified trading history
- Network insight (aggregate and anonymised only): category price benchmarks, lead-time reliability,
  fill-rate percentiles. **Never expose one tenant's data to another** — a retailer must never see
  another retailer's volumes, and a supplier must never see a competitor's prices. This is the single
  most dangerous surface in the product for trust, and it has its own security suite.
- Onboarding: a supplier can join in a lightweight mode (catalogue and orders only) without adopting
  full Vuma, and upgrade later

### The supplier portal
A web application on `VumaRetail.PublicApi`: catalogue and price management, connection and code
management, incoming orders board, dispatch and ASN capture, invoices and payments, retailer directory,
and analytics on their own sales. Built with the Stage 08b design system, so it is the same product.

## Business rules
- **Consent is mutual and revocable.** No data flows between tenants without an active connection, and
  either side can end it. Ending a connection stops future flow and leaves historical documents intact
  on both sides.
- **A supplier never writes directly into a retailer's data.** Everything arrives as a proposal the
  retailer accepts — prices, catalogue changes, everything. The only exception is the ASN pre-populating
  a GRN, which the retailer still confirms line by line before it posts.
- Cross-tenant reads are scoped to the connection and to the specific documents that connection created.
  There is no query path from one tenant to another's general data, and an architecture test enforces it.
- Both tenants keep their own complete record. If the network is unavailable, both sides still operate
  on what they have and reconcile when it returns.
- Licensing: a retailer in read-only (ADR-028) can still **see** connect data but cannot order or pay.
  Suppliers see a neutral status, never the retailer's billing state.

## Tests / acceptance
- Connection lifecycle across two real tenants, including code redemption, suspension and ending
- A supplier code issued, redeemed by a retailer, relationship live with the right price tier
- Price publication: future-dated list lands as a proposal, diff is correct, partial acceptance applies
  only the accepted lines, rollback reverses cleanly
- Auto-accept threshold: a 3% change auto-applies, a 30% change is held for review
- Full order round trip: retailer PO → supplier sales order → confirmation with a substitution → ASN →
  retailer GRN pre-populated → short-delivery claim → credit note on both sides
- Payment: retailer pays, retailer AP and supplier AR both post correctly in the same operation, and a
  failed payment leaves both sides consistent
- **Isolation suite (the critical one):** attempt to read another tenant's items, prices, stock, orders,
  customers and volumes through every Connect endpoint, filter, expansion and sort parameter — all must
  fail. Attempt to reach a retailer's data through a supplier-issued user grant beyond its scope — must
  fail.
- Network insight aggregation: verify no figure can be reverse-engineered to a single tenant, including
  where only two tenants are in a category
- Both-sides-offline: each tenant continues on local data and converges on reconnect

## Exit checklist
- [ ] Two tenants can connect via a supplier-issued code and trade end to end without leaving Vuma
- [ ] Price publication → proposal → acceptance → cost update proven, with rollback
- [ ] ASN pre-populates a GRN and receiving is scan-and-confirm
- [ ] Payment posts to both ledgers in one operation with correct remittance
- [ ] Cross-tenant isolation suite green — no exceptions, no known gaps
- [ ] Supplier portal live on the Stage 08b design system
- [ ] Settlement provider stubbed behind an interface with the regulatory position documented
- [ ] Replication registry updated, `docs/PROGRESS.md` + ADRs updated, committed
