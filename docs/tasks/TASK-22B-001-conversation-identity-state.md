# TASK-22B-001 — Conversational identity and state

**Status:** COMPLETE · **Stage:** 22b · **Type:** Domain / application / infrastructure / test

## Objective

Bind inbound channel addresses to tenant-owned contacts, enforce verification and consent before
conversation work, and persist an explicit conversation state machine.

## Current evidence

- Durable conversation-turn idempotency keys are tenant/conversation scoped and protected by a unique
  database index; replay survives process restart. Concurrent duplicate inserts are converted to a
  durable no-op by `TryAddTurnAsync`, rather than surfacing a provider unique-key error. Escalation
  and transcript reads now require the tenant explicitly as well as relying on EF filters.

- `ContactBinding`, `VerificationChallenge`, `Conversation`, and `ConversationTurn` exist.
- Registry binding/challenge persistence and company conversation persistence are wired.
- Verification, 24-hour freshness, three-attempt limit, rate limiting, escalation, and idempotency
  primitives are covered by conversation unit tests.
- Inbound `STOP` now withdraws consent through the registry service before any transcript is written.
- Stage 22b conversation migration Up/Down passes on real PostgreSQL.
- `ConversationAccountScope` now persists an explicit binding/company/customer-account boundary in
  the registry, with permission-gated add/list endpoints and a reversible registry migration.
- Scope migration Up/Down passes on real PostgreSQL; the conversation unit slice is 17/17 green.
- All account-backed document intents now reduce persisted grants to the active operating company
  before resolving account or document references; conversation handler tests remain 32/32 green.
- Real HTTP webhook coverage rejects an invalid signature, gives an unbound sender onboarding only,
  and refuses a binding owned by another tenant; the focused API checks pass 3/3.
- Transcript data classification, tenant ownership, retention policy and permission-gated audit
  handling are recorded in `docs/SECURITY.md` §5.

## Remaining work

- None. Transcript retention classification/policy and permission-gated visibility are recorded in
  `docs/SECURITY.md` §5.

## Verification

2026-09-17: PostgreSQL-backed HTTP coverage now sends a signed webhook for a verified, consented
binding, proves a tenant-owned transcript turn is persisted, and proves the same conversation is
empty when read with another tenant identifier. Existing API mapping keeps transcript reads behind
`conversations.transcript.view`; account/company scope isolation is covered by the six intent tests.
Focused integration test passes **1/1** and the conversation unit slice remains green.

## Definition of done

Identity scope is structural, consent withdrawal is immediate and durable, and state/transcript
behavior is proven through API and PostgreSQL integration tests.
