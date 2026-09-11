# TASK-22B-001 — Conversational identity and state

**Status:** IN_PROGRESS · **Stage:** 22b · **Type:** Domain / application / infrastructure / test

## Objective

Bind inbound channel addresses to tenant-owned contacts, enforce verification and consent before
conversation work, and persist an explicit conversation state machine.

## Current evidence

- Durable conversation-turn idempotency keys are tenant/conversation scoped and protected by a unique
  database index; replay survives process restart. Escalation now persists the conversation state.

- `ContactBinding`, `VerificationChallenge`, `Conversation`, and `ConversationTurn` exist.
- Registry binding/challenge persistence and company conversation persistence are wired.
- Verification, 24-hour freshness, three-attempt limit, rate limiting, escalation, and idempotency
  primitives are covered by conversation unit tests.
- Inbound `STOP` now withdraws consent through the registry service before any transcript is written.
- Stage 22b conversation migration Up/Down passes on real PostgreSQL.
- `ConversationAccountScope` now persists an explicit binding/company/customer-account boundary in
  the registry, with permission-gated add/list endpoints and a reversible registry migration.
- Scope migration Up/Down passes on real PostgreSQL; the conversation unit slice is 17/17 green.

## Remaining work

- Connect the new persisted scope to the contact-to-account/company resolver consumed by every intent
  handler.
- Add end-to-end inbound webhook tests proving tenant and account isolation.
- Record transcript retention and CRM visibility evidence.

## Definition of done

Identity scope is structural, consent withdrawal is immediate and durable, and state/transcript
behavior is proven through API and PostgreSQL integration tests.
