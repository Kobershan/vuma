# Stage 22b verification evidence

Date: 2026-09-18

Repository-owned conversational commerce is complete and bounded by `docs/CHATBOT.md`:

- only the six allow-listed intents are classified and routed;
- the classifier has no repository or data access, and replies are assembled from API facts;
- binding verification, consent, account/company scope, one-time document tokens and transcript
  isolation are enforced at the application and persistence boundaries;
- inbound WhatsApp signatures, Twilio signatures, normalized email, STOP, rate limits, lockout,
  injection handling and duplicate delivery are covered;
- document, POD, credit-note and pro-forma operations delegate to their owning modules and fail closed
  when a required owner boundary is not available;
- router idempotency is serialized per key, so concurrent duplicate submissions invoke a handler once;
- conversation delivery is audited through the existing transport boundary and does not introduce a
  second WhatsApp or SMTP client.

Evidence:

- focused conversation and marketing unit tests: **67 passed, 0 failed**;
- conversation safety regression: **25 passed, 0 failed**;
- conversation intent-handler regression: **10 passed, 0 failed**;
- migration up/down, API, architecture and PostgreSQL evidence is recorded in the linked task logs.

Provider-backed six-intent execution and the specialist runtime safety agent require live deployment
credentials/tooling unavailable in this environment. They remain an explicit external verification
boundary; repository tests do not fabricate that evidence.
