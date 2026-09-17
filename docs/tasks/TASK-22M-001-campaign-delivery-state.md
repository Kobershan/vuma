# TASK-22M-001 — Campaign and outbound-message state

Status: COMPLETE for campaign, journey, attribution, consent, and provider-result state slices; shared
outbox/provider callback deployment remains
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

2026-09-14: The application dispatch boundary now rechecks consent before transport, suppresses
withdrawn recipients, and applies provider delivery results. Focused marketing tests pass **19/19**.

2026-09-15: A configured HTTPS marketing transport adapter is now registered for production delivery.
It sends durable message identity and idempotency metadata, requires a provider event identity,
supports deployment-provided bearer credentials, and remains fail-closed when no endpoint is configured.
Focused transport tests pass **2/2**; provider-specific callback deployment remains environment configuration.

2026-09-15: Company-scoped attribution recording/listing and journey operator routes are implemented;
marketing-focused tests remain green at **19/19**.

2026-09-15: The durable delivery worker now has bounded due-queue execution coverage; successful
delivery and retryable transport failure state are verified in the marketing unit suite (**26/26**).

2026-09-17: Retry scheduling is now durable on `marketing.outbound_messages`: failed deliveries use
bounded exponential backoff, due-queue reads exclude messages before `next_attempt_at_utc`, successful
provider results clear the retry timestamp, and provider event IDs are unique per tenant/company.
Migration `20260917041436_Stage22MarketingRetryScheduling` passes PostgreSQL up/down/up execution;
the focused marketing unit suite passes **27/27**.

2026-09-17: `MarketingDeliveryHostedService` is registered by StoreServer. It enumerates active
tenant companies, creates a fresh tenant/company scope per company, and dispatches bounded batches
through the existing durable worker; a failure for one company is logged without stopping the
remaining companies. StoreServer and the focused marketing suite remain green (**27/27**).

## Follow-up findings

The durable shared delivery outbox and provider callback deployment remain open. Outbound rows now
record retryable transport attempts and bounded failure reasons while remaining queued for a later
worker pass. The provider adapter boundary is now implemented and fail-closed when unconfigured. Provider
event identity and payload fingerprints now make identical callback replay a no-op and changed
content reuse a stable conflict; migration `20260914034549_Stage22MarketingProviderResults` adds
the durable fields.
