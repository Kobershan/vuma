# TASK-30B-002 — Metering, billing, dunning, and analytics

Status: IN_PROGRESS — vendor usage and billing calculation boundary implemented
Stage: 30b
Type: Control-plane application, billing, analytics

## Objective

Ingest aggregate usage safely, calculate subscription charges without a tenant ledger dependency,
and pause dunning when notification delivery cannot be proven.

## Scope and constraints

Usage is keyed by `(nodeId, period)` and duplicate rollups are no-ops. Only aggregate counters are
accepted. Billing uses plan prices, transaction overage and cycle-day proration. Payment methods are
represented by gateway tokens only; card data is never accepted by this boundary. Dunning cannot
advance while any recorded notice is undelivered.

## Work log

2026-09-15: Added `UsageRollupAggregator`, `BillingCalculator`, token-only payment contract and
delivery-aware `DunningTracker`. Dedicated tests pass **3/3**. Full subscription gateway integration,
virtual-clock notification scheduling, invoice collection, partner isolation and 500-node soak
acceptance remain.
