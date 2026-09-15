# TASK-22B-002 — Classifier, composer, and six intents

**Status:** IN_PROGRESS · **Stage:** 22b · **Type:** Application / integration / test

## Objective

Classify only the six allow-listed intents and execute each through the owning module API, with no
model data access and no unscoped customer lookup.

## Current evidence

- `KeywordIntentClassifier` and `TemplateReplyComposer` are deterministic and data-free.
- `ConversationIntentRouter` rejects unknown/low-confidence intents and deduplicates idempotency keys.
- The six intent enum values and safety tests exist.
- `OrderStatusIntentHandler` is now registered and queries orders only through the persisted
  binding scope; the repository exposes a partner-constrained query rather than an in-memory
  post-filter.
- `InvoiceCopyIntentHandler` resolves a real invoice by number and verifies its customer against the
  binding's authorized partner scope before minting a delivery token.
- Statement, credit-note, and pro-forma order handlers use the persisted binding scope and recheck
  tenant/company ownership before issuing or submitting a result. Conversation handler tests pass
  **32/32**.
- Injection text is treated as message data: the deterministic fallback preserves the allow-listed
  intent without extracting an unauthorized account identifier.

## Remaining work

- Add result-number/date post-checking and tests for prompt-injection text.

## Definition of done

All six handlers use scoped module APIs, every action confirms before submission, and no outbound
fact can be introduced by the classifier or composer.
