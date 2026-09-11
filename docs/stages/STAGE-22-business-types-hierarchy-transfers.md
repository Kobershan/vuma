# STAGE 22 — Business identity, hierarchy, shared premises and stock transfers

**Status:** IN_PROGRESS (2026-09-11) · **Depends on:** 06c, 06e, 08/08c, 09b, 12 · **Reference:** `docs/DECISIONS.md` ADR-099, ADR-116, `docs/MULTI_COMPANY.md`

## Objective

Make the installation's business shape explicit without schema variants. Vuma has one registry and one structurally identical database per legal company. Stage 22 adds registry relationships, owned-store hierarchy, a compensatable inter-company transfer saga, franchise supplier relationships, shared-premises SKU routing, and independent pricing domains. It never joins company databases in one transaction.

`company_id` is the legal identity and only company join key. The Control App issues it before local onboarding; the local installer consumes it and provisions `vuma_<tenant>_<company_id>`. `business_id` is an optional grouping label used only by relationship tables. `store_code` is a validated display label, never an identity.

## Coherent model

The registry stores `business_type`: `single_business`, `multi_location`, or `group_business`. All company databases use the same schema and migrations, so changing type is registry-only. Single Business includes independent and franchised stores. Multi-Location links owned stores, regional companies and head-office companies. Group Business links two or more real companies at one premises and one till.

`group_hierarchy_node(tenant_id, company_id, business_id, node_type, ownership_type, parent_node_id, store_code, stock_holding)` is registry-owned. Nodes are `store`, `regional`, or `head_office`; ownership is `owned` or `franchised`. Regional and HO are ordinary companies with ordinary databases. New nodes default `stock_holding = true`. Links enforce one parent, no cycles, tenant isolation and group-prefix store-code validation. Control is the downward tree for owned nodes. Visibility is a dedicated stock-on-hand-only projection across owned nodes with no cost, margin, financial, supplier-credit or GL field. Franchised nodes are structurally excluded from both dimensions.

## Transfer engine

The registry owns transfer intent, append-only audit and saga records; each company owns reservations, stock ledger, batches, expiry and serials. A handler opens one company context only.

`Requested → Checked → (direct to holding store | Regional approval) → Accepted/Declined → Reserved → Picked → Shipped → InTransit → Received → Reconciled`

The value check uses total value and `group_settings.transfer_value_threshold`. At/above threshold Regional approves before the holding store is notified. Acceptance immediately appends a source `StockReservation`. Pick/ship carries batch, expiry and serial references and fixes `unit_cost_at_transfer` using `sender_cost`, `group_standard_cost`, or `landed_cost`. InTransit is excluded from both parties’ on-hand. Receipt publishes quantity to both sides; reconciliation computes discrepancy, requires reason/owner for non-zero values, posts GL by configurable `sender|receiver|split|held_for_review`, and audit-logs overrides.

Partial fulfillment creates a cancellable remainder for the requester. Reverse transfers reference the original. Every queue transition emits in-app and push/email notification. Suggested SLAs are Regional approval 4 business hours with HO escalation, holding-store decision 24 hours with Regional nudge and no auto-accept, and discrepancy review 3 business days. Central buying reuses this exact engine with HO/Regional as `holding_company_id` and pre-accepted status.

Transfer records are append-only. Saga legs are idempotent and compensatable under ADR-116; failed work appends releases/reversals. Delivery notes are printable/exportable with SKU, quantity, batch/expiry, sender, receiver, date and driver reference. They are goods-movement documents, not VAT invoices; wording remains an accountant confirmation item.

## Franchise, shared premises and pricing

A franchise remains a Single Business, never a hierarchy node, transfer participant or visibility contributor. The brand is an internal supplier in the franchise database; normal PO, VAT invoice, GL and goods receipt remain unchanged. Franchise pricing is flat wholesale per SKU with a franchisee-company override fallback.

`premises_sku_routing(premises_id, sku_or_barcode, company_id)` is explicit at catalog setup. One bare barcode routes to one company; legitimate duplicate ownership requires a company-specific barcode/variant. POS shows one basket/payment but commits each line’s price, stock and GL in its owning company through saga legs. Receiving routes by supplier/PO.

Group pricing is retail sell-to-customer pricing for owned stores (HO base, local override). Franchise pricing is brand wholesale pricing. Shared-premises pricing is each company’s independent retail list. `unit_cost_at_transfer` is never customer-facing.

## Acceptance, tests and open decisions

Tests cover pre-issued identity, schema invariance, type changes without migration, hierarchy cycle/parent/prefix rules, the cost-free projection guard, threshold routing, reservation races, batch/expiry/serial/costing, in-transit stock, partial/reverse transfers, discrepancy GL/audit, notifications, idempotent saga legs, delivery notes, ordinary franchise POs and shared-till split posting. Architecture tests continue to reject two company contexts in one handler.

Open decisions: confirm SLA/calendar; franchise tiering beyond flat-plus-override; whether a bare SKU can span companies at one premises; accountant wording/retention; tenant defaults for threshold, currency rounding, costing and discrepancy owner; and whether live type changes need approval workflow.

See `docs/tasks/TASK-22-01` through `TASK-22-18`; identity and registry constraints precede hierarchy, transfer saga/local stock legs, then shared-premises routing and pricing.

## Current implementation evidence

The registry business, hierarchy, owned-stock projection, premises routing, transfer lifecycle,
company-local reservation/shipment/receipt saga legs, sender-cost capture, and reversible migrations
are implemented. Stage 13's warehouse ledger is reused for transfer movements; no second inventory
ledger was introduced. Related remainder and reverse transfers are now represented as normal registry
transfers with explicit relation metadata, protected API routes, replay-safe uniqueness, and immutable
goods-movement delivery notes. Transfer lines also preserve optional batch/expiry/serial identities,
including them in delivery-note snapshots and saga payloads; serial uniqueness and quantity-one rules
are enforced by the domain and database.
Independent group, franchise-wholesale, and shared-premises price rows are now persisted with
permission-gated registry APIs and ownership/occupancy validation.

Verified on 2026-09-11: focused Stage 22 unit/domain tests and PostgreSQL migration/registry tests are
green; the complete repository integration suite is recorded separately in the handoff. The stage
remains open because company-ledger batch/serial enforcement, discrepancy GL/audit posting,
notifications/SLA execution, sales/POS consumption of the independent prices, and shared-till split
posting still require their own implementation and acceptance evidence.
