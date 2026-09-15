# STAGE 22 — Marketing Automation

**Status:** IN_PROGRESS — consent-aware marketing delivery policy, tenant/company-scoped campaign/outbound-message persistence, guarded commands, API routes, configured provider transport and retryable delivery metadata are implemented and tested; journeys, provider callbacks, durable shared outbox deployment and full acceptance remain · **Depends on:** 19; transport integration with 05, 20, 21; prerequisite for 22b · **Reference reading:** [CRM stage](STAGE-19-crm.md), [conversational commerce](STAGE-22b-conversational-commerce.md), [security/privacy](../SECURITY.md) §5; [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

> **Audit correction (2026-09-14):** Stage 22 remains **IN_PROGRESS**. Consent and campaign/message
> foundations are present, but durable delivery processing, configured provider transport, callbacks,
> journeys and full PostgreSQL/API acceptance are not complete.

Deliver consent-aware campaigns and journeys through one shared email/SMS/WhatsApp transport. Campaign attribution uses real channel events and explicit privacy controls.

## What this stage does not own

The existing STAGE-22-business-types-hierarchy-transfers.md is a separate historical workstream with existing TASK-22-* records; preserve it. Marketing planning IDs use 22M to avoid collisions. CRM owns consent, Sales owns promotions, and 22b consumes this stage's transport.

## Deliverables

### Domain and client model

`MarketingCampaign`, `JourneyDefinition`, `JourneyEnrollment`, `OutboundMessage`, `DeliveryEvent`, `AttributionEvent`. Start in `src/VumaRetail.Domain/Marketing/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

`ScheduleCampaignCommand`, `EnrollJourneyCommand`, `DispatchCampaignMessageCommand`, `ApplyDeliveryEventCommand`, `SuppressRecipientCommand`. Ports: `IMarketingRepository`, `IMessageTransport`, existing `IConsentService`. Query handlers expose scoped DTOs; mutating handlers carry explicit side-effect and entitlement classification. Financial integration uses the existing posting service, and approval uses Stage 05.

### Infrastructure

Add mappings/repositories for the listed types under `src/VumaRetail.Infrastructure/` where server persistence is needed, with migrations and real PostgreSQL tests. Local client persistence is separate from company books. Assign each replicated type one documented direction/authority and retry policy; use the shared outbox/inbox.

### API

`/api/v1/marketing/campaigns`, `/journeys`, `/deliveries`, `/suppressions`, `/webhooks/{provider}`. These are planned contracts: publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. For server modules, routes live in `src/VumaRetail.Web/`; customer-facing DTOs stay in `src/VumaRetail.PublicApi/`. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `marketing.view`, `marketing.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Resolve consent from CRM immediately before delivery, including queued messages; opting out overrides a previously selected audience.
2. Use a single durable delivery outbox shared with conversational commerce; idempotency is campaign-step-recipient-version scoped.
3. Separate transactional and marketing messages; transactional classification cannot disguise promotional content.
4. Default quiet hours are 20:00–08:00 in the recipient/tenant timezone; default cap is one marketing message per channel per 24 hours, configurable and audited.
5. Use provider template IDs and approved placeholders; signed callbacks have event identity, replay controls and delivery status transitions.
6. Loss of cloud or transport delays messages; it never blocks POS or extends consent.
7. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [~] 22M-P01: Implement campaign/journey state, audience snapshots and consent/suppression rules.
  Consent-aware campaign/outbound state, explicit WhatsApp consent, tenant/company persistence,
  guarded commands, replay protection and suppression are implemented; audience snapshots and
  durable delivery processing remain.
- [~] 22M-P02: Add shared transport adapters, delivery outbox and signed provider callbacks.
  Provider-result state now persists event identity and payload fingerprints; identical callbacks
  are idempotent and changed-content replays are rejected. Shared transport adapters and delivery
  worker remain. Outbound messages now persist channel/classification metadata, and company-scoped
    campaign/message operator reads and due-queue listing are exposed. A consent-aware application
    dispatch boundary now suppresses withdrawn recipients before invoking a transport and applies
    provider results idempotently. A configured HTTPS transport adapter is registered with
    deployment-provided bearer credentials and a fail-closed no-endpoint default; focused transport
    coverage passes 2/2. Provider-specific callback deployment remains.
- [~] 22M-P03: Add attribution queries, operator APIs and opt-out/replay/timezone acceptance.
  Company-scoped attribution recording/listing and operator routes are implemented; PostgreSQL and
  provider callback deployment acceptance remain.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

Implemented evidence: the marketing-focused unit run passes **19/19**, covering channel-specific consent,
transactional bypass, WhatsApp fail-closed behavior, and recipient-timezone quiet-hours scheduling.

2026-09-15: Attribution recording and company-scoped listing are exposed through the marketing API;
the focused marketing suite and runtime route verification cover the operator boundary.

2026-09-15: Durable due-queue execution is covered through the marketing worker: processing is
bounded by the requested company/limit, successful provider delivery transitions the queued row to
sent, and transport failures remain queued with an incremented attempt count and bounded failure
reason. Marketing unit tests pass **26/26**.

2026-09-13: Added validated `MarketingCampaign` scheduling and append-only `OutboundMessage`
metadata with per-recipient idempotency keys and suppression state. Marketing-focused tests now pass
8/8; campaign persistence, audience snapshots, transports, signed callbacks and APIs remain open.

2026-09-13: Added a dedicated `MarketingWhatsApp` consent purpose; WhatsApp marketing now requires
that explicit purpose immediately before delivery rather than being treated as another channel.

2026-09-14: Marketing create/queue routes now bind the request company, and campaign/message state
transitions accept an explicit company selector before dispatch. StoreServer build passes with
**0 errors**; provider-result event identity is now durable and signed callback input is replay-safe.
Campaign and message operator reads now return not-found across company boundaries.
Queued-message listing is bounded, ordered by scheduled time, and supports a due-only filter.
OpenAPI/runtime route verification passes **2/2**, covering the campaign/message operator reads,
signed provider callback route, and unsigned-callback rejection.

2026-09-14: Added the application dispatch boundary. It rechecks consent at execution time,
suppresses withdrawn recipients without calling transport, and applies delivered provider identity
and payload fingerprints to the outbound state. Focused marketing tests pass **19/19**.

- `Opt_out_after_queue_prevents_send`: queue 100 recipients, 3 opt out before dispatch; only 97 are sent.
- `Same_step_delivers_once`: replay one campaign step five times; one provider idempotency key and one logical delivery.
- `Quiet_hours_follow_recipient_zone`: a marketing message scheduled at 21:00 local moves to 08:00 next day.
- `Conversation_uses_shared_suppression`: a contact opted out through a campaign is suppressed in the corresponding marketing conversation flow.
- `Other_tenant_and_unauthorized_company_are_denied`: authenticated tenant A/company A cannot read, mutate, export or enqueue for tenant B/company B by changing an ID.
- `Replay_with_different_content_is_rejected`: reuse a completed operation ID with changed input; return a stable conflict and preserve the original result.
- Execute migration Up/Down on a disposable database, permission-denial tests on every high-risk route and module read-only behavior. Client-only changes mark database checks not applicable with a reason.

## Exit checklist

- [ ] Every listed rule and scenario has executed evidence, including outage/replay and authorization.
- [ ] Planned API routes are verified against the actual host's OpenAPI and real client contracts.
- [ ] Per-company accounting/stock, retention and audit requirements are satisfied where applicable.
- [ ] Seed/demo, migration reversibility, backup implications and module replication registration are evidenced.
- [ ] Relevant specialist reviews from [AGENTS](../AGENTS.md) are recorded; missing tooling is UNVERIFIED, not an invented review.
- [ ] `CLAUDE.md` §8 is met, measured results are recorded and unresolved release blockers remain open.

**Verification boundary:** this document was reviewed for scope and links only. No stage implementation, live API, UI, migration or production vendor integration was certified in the 2026-09-12 audit.
