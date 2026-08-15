# VUMA CONNECT API — supplier ↔ retailer trading network

The cross-tenant trading contract, served from `VumaRetail.PublicApi` alongside the storefront and
loyalty APIs. Built in Stage 21b. Read `docs/API_STANDARDS.md` first; this covers what is different
because these calls cross a tenant boundary.

Base path: `https://api.vumaretail.app/connect/v1`

## 1. The rule that governs everything here

> **No data crosses a tenant boundary without an active, mutually accepted connection, and even then
> only the documents that connection created.**

There is no query path from one tenant to another's general data. Not filtered — structurally absent,
with its own DTO set and an architecture test that fails the build if a Connect endpoint can reference
an internal entity. A retailer must never see another retailer's volumes; a supplier must never see a
competitor's prices.

Auth: each side authenticates as itself with its own tenant credentials. Every Connect call carries a
`connectionId`, and the server verifies that the caller is a party to it and that it is `Active` before
anything else happens.

## 2. Connections

```
POST /connections/invite            { counterpartyEmail|tenantCode, role, proposedTerms }
POST /connections/codes             supplier issues a code
  { uses: 1|n, expiresAt, priceTier, territory, grantsPortalAccess }
  → { code: "VUMA-SUP-7K2M-9QX4", shareUrl }
POST /connections/redeem            { code }  retailer redeems → relationship pending or active
POST /connections/{id}/accept       { agreedTerms }
POST /connections/{id}/suspend      { reason }
POST /connections/{id}/end          { reason }   future flow stops; history stays on both sides
GET  /connections                   my connections, either role, with status and terms
GET  /connections/{id}/terms        price tier, payment terms, credit limit, lead times,
                                    delivery days, minimum order, account references
```

Supplier-issued codes are the growth mechanism: a rep hands one to a shop owner and the relationship is
live before they leave. Codes are single or multi use, expiring, and can carry a price tier and
territory.

**Supplier-granted portal users:** a code with `grantsPortalAccess` also creates a named contact at the
retailer with access to the supplier's ordering portal. The retailer's own admin can always list and
revoke these (`GET|DELETE /connections/{id}/granted-users`) — a supplier can never quietly hold access
inside a retailer's tenant.

## 3. Catalogue and prices (supplier publishes, retailer consumes)

```
POST /catalogue/publish             { items[], packs[], media[], effectiveFrom, versionNote }
POST /catalogue/{version}/rollback  { reason }  retailers are alerted, never silently corrected
GET  /catalogue?connectionId=       what this retailer is entitled to see
GET  /catalogue/delta?since=        incremental pull

POST /price-lists/publish
  { tier|territory|connectionId, effectiveFrom, expiresAt, lines: [ { supplierSku, price,
    breaks[], moq, packSize, leadTimeDays } ], versionNote }
GET  /price-lists/incoming          retailer: published lists awaiting decision
POST /price-lists/{id}/accept       { lines?: [...] }   full or partial acceptance
POST /price-lists/{id}/reject       { reason }
GET  /price-lists/{id}/diff         line-by-line before → after against my current costs

POST /catalogue/mapping             { supplierSku, myItemId }   remembered thereafter
GET  /catalogue/mapping/suggestions fuzzy matches on first connect
```

**A published price is a proposal, never an update.** It lands in the retailer's Stage 11 import preview
with a full diff. Auto-accept is opt-in per supplier and per category, with a percentage-change
threshold above which review is always required. This guarantee is why retailers are willing to connect
at all — remove it and the network dies.

## 4. Orders

```
POST /orders                        retailer places; supplier receives it as a sales order
GET  /orders?role=buyer|seller&status=
POST /orders/{id}/confirm           { lines[], substitutions[], promisedDate }
POST /orders/{id}/reject            { reason }
POST /orders/{id}/amend             pre-confirmation only
POST /orders/{id}/cancel            { reason }   subject to the agreed terms

POST /orders/{id}/dispatch          the ASN — line quantities, batches, serials, expiry, packages,
                                    carrier, tracking
GET  /orders/{id}/asn               retailer pulls it to pre-populate the GRN
POST /orders/{id}/receipt           retailer confirms actual received quantities
POST /orders/{id}/claim             { type: short|damaged|wrong|expired, lines[], evidence[] }
POST /claims/{id}/resolve           { outcome, creditNoteId? }  posts on both sides at once
```

One order, two views, one status. Not an email with a PDF attached.

## 5. Invoicing and payment

```
POST /invoices                      supplier issues against a dispatched order
GET  /invoices?role=&status=
GET  /invoices/{id}/match           three-way match state on the retailer's side
POST /payments                      { invoiceIds[], method, amount, idempotencyKey }
  → posts to retailer AP and supplier AR in one operation, generates remittance
GET  /payments/{id}
GET  /statements?connectionId=&period=   supplier statement matched to retailer payables,
                                         exceptions only
GET  /settlements?period=           network fees, held funds, payout schedule
```

Payment is idempotent and atomic across both ledgers. A failure leaves both sides consistent — there is
no state where one side shows paid and the other does not.

**Vuma is not a bank.** Funds move through a licensed provider behind `IPaymentGateway` and
`ISettlementProvider`; Vuma orchestrates and records. Confirm the regulatory position in
`docs/compliance/` before any real money flows.

## 6. Discovery

```
GET /directory/suppliers?category=&region=&deliveryDay=&minOrder=
GET /directory/suppliers/{id}       profile, catalogue preview, terms, verified trading history
GET /insights/benchmarks?category=  aggregate, anonymised, k-anonymity enforced
```

Benchmarks are suppressed entirely where too few tenants would make a figure attributable — including
the case where only two tenants trade in a category. Tested for.

## 7. Webhooks

Signed HMAC-SHA256, at-least-once, deduplicate on `eventId`.

```
connection.requested   connection.accepted    connection.ended
catalogue.published    price_list.published   price_list.accepted   price_list.rejected
order.placed           order.confirmed        order.dispatched      order.received
claim.raised           claim.resolved
invoice.issued         payment.received       payment.failed
```

## 8. Degradation

If the network is unavailable, both tenants keep operating on what they already hold: the retailer's
last-accepted prices stay in force and orders queue; the supplier's order board catches up on
reconnect. Neither side's trading depends on Connect being up.

A retailer in licence read-only (ADR-028) can still **read** Connect data — catalogue, prices, order
history — but cannot place orders or pay. The supplier sees a neutral "unavailable" status and never the
retailer's billing state.
