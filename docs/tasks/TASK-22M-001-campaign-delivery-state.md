# TASK-22M-001 — Campaign and outbound-message state

Status: IN_PROGRESS  
Stage: 22  
Type: Domain

## Objective

Define scheduled marketing campaigns and append-only outbound-message state with explicit
per-recipient idempotency and suppression transitions.

## Scope

Campaign scheduling/cancellation invariants, queued-message metadata, suppression and sent-state
transitions. Consent evaluation, audience snapshots, transports, callbacks and APIs are follow-up
tasks. Campaigns and outbound messages now have tenant/company-scoped EF mappings and reversible
migration `20260913221156_Stage22MarketingPersistence`.

## Verification

2026-09-13: Marketing-focused unit tests pass 8/8, including future-only scheduling and explicit
suppression state. StoreServer Release build passes with 0 errors; the migration contains the
`marketing.campaigns` and `marketing.outbound_messages` tables with unique idempotency keys.

## Follow-up findings

The durable shared delivery outbox, provider adapters, signed callback replay protection and
operator endpoints remain open.
