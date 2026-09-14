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
tasks. Campaigns and outbound messages now have tenant/company-scoped EF mappings, persistence
repositories, company-guarded commands, and manage-protected API routes. Idempotency replay,
suppression and sent-state transitions enforce the active company; a reused key from another
tenant/company is rejected. The reversible migration is
`20260913221156_Stage22MarketingPersistence`.

## Verification

2026-09-13: Marketing-focused unit tests pass 12/12, including future-only scheduling, explicit
suppression state and cross-company replay/suppression refusal. StoreServer and CloudApi builds pass with 0 errors; the migration contains the
`marketing.campaigns` and `marketing.outbound_messages` tables with unique idempotency keys.

2026-09-14: The focused marketing run passes **16/16** after adding durable provider event identity
and payload fingerprints; identical callbacks are no-ops and changed-content reuse is rejected.

## Follow-up findings

The durable shared delivery outbox, provider adapters and operator endpoints remain open. Provider
event identity and payload fingerprints now make identical callback replay a no-op and changed
content reuse a stable conflict; migration `20260914034549_Stage22MarketingProviderResults` adds
the durable fields.
