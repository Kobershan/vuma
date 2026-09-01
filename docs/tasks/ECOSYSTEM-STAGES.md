# TASK-21B-001 — Implement Vuma Connect relationships and catalogues

## Status
NOT_STARTED
## Stage
Stage 21b
## Objective
Implement supplier/retailer relationships, codes, catalogue publication, price distribution, proposals, and acceptance.
## Why
Vuma Connect enables B2B trading without leaving Vuma.
## Scope
Trading relationship and catalogue/order flows described in Stage 21b.
## Out of Scope
Transport, vendor billing, and undocumented network behavior.
## Dependencies
Stages 07, 12, 14, 21.
## Relevant Files
`docs/stages/STAGE-21b-vuma-connect.md`, `docs/API_CONNECT.md`, `docs/DATA_MODEL.md` connect section.
## References
ADR-056; Stage 21b Deliverables, business rules, tests.
## Implementation Requirements
Preserve supplier-issued connection, price proposal/acceptance, cost update rollback, ASN/GRN, and scan-confirm receipt requirements.
## Acceptance Criteria
Two tenants connect, publish price, accept proposal, and receive goods end to end with rollback.
## Tests Required
Cross-tenant isolation, catalogue, ordering, ASN/GRN, and rollback tests.
## Edge Cases
Revoked relationship, stale price, duplicate message, and partial receipt.
## Security Considerations
Strict tenant isolation and supplier/retailer authorization.
## Architectural Impact
Adds the Connect network boundary.
## Definition of Done
Relationship/catalogue/order foundations are tested.
## Follow-up

# TASK-21B-002 — Implement Connect settlement and supplier portal

## Status
NOT_STARTED
## Stage
Stage 21b
## Objective
Implement payment/settlement abstraction, remittance, discovery, portal, design-system integration, and replication.
## Why
Trading must settle correctly while keeping provider/regulatory choices isolated.
## Scope
Settlement interface/stub, portal, discovery, permissions, API, seed, and docs.
## Out of Scope
Choosing or integrating a live regulated provider beyond the specified interface.
## Dependencies
TASK-21B-001; Stage 08b design system.
## Relevant Files
Stage 21b Payment/discovery/portal and Exit checklist; `docs/API_CONNECT.md`.
## References
ADR-056; `docs/EXECUTION_STANDARD.md`.
## Implementation Requirements
Payment posts to both ledgers with remittance; settlement is behind an interface and regulatory position is documented.
## Acceptance Criteria
Portal is live on the design system, isolation suite passes, and replication registry is updated.
## Tests Required
Settlement, remittance, portal/API, isolation, migration, and seed tests.
## Edge Cases
Settlement failure, duplicate payment, and provider unavailable.
## Security Considerations
Tenant/network boundary and payment-token handling.
## Architectural Impact
Exposes the Stage 21 payment abstraction reused by 30b.
## Definition of Done
All Stage 21b exit items have evidence.
## Follow-up

# TASK-22B-001 — Implement conversational identity and state machine

## Status
NOT_STARTED
## Stage
Stage 22b
## Objective
Implement contact binding, OTP verification/lockout, conversation state, escalation, and safe unbound-sender behavior.
## Why
No conversational intent may access data before verified identity and scope.
## Scope
Build-list B1–B3 and C1, registry/company scoping, persistence, and tests.
## Out of Scope
Intent execution and transport delivery.
## Dependencies
Stages 19, 22, 06d, 06e.
## Relevant Files
`docs/stages/STAGE-22b-conversational-commerce.md`, `docs/CHATBOT.md`, `docs/SECURITY.md` §POPIA.
## References
ADR-129–132; Stage 22b Parts A–C and business rules 2–5.
## Implementation Requirements
OTP is hashed/expiring/max-three attempts; lockout notifies tenant; unbound senders receive no account existence signal; states timeout/escalate.
## Acceptance Criteria
Unbound, expired, wrong-OTP×3, cross-account, and injection fixtures pass.
## Tests Required
Identity, lockout, scoping, state transitions, and injection tests.
## Edge Cases
Expired verification, multiple companies, and replayed inbound message.
## Security Considerations
POPIA, structural scoping, and message-as-data.
## Architectural Impact
Creates conversation security boundary.
## Definition of Done
Identity and state machine are safe and independently tested.
## Follow-up

# TASK-22B-002 — Implement classifier, composer, and six intents

## Status
NOT_STARTED
## Stage
Stage 22b
## Objective
Implement fake-backed classification/composition and statement, invoice, status, pro-forma order, credit-note, and POD intents.
## Why
The assistant must classify and phrase API results without computing or authorizing beyond existing modules.
## Scope
Build-list C2–C4 and D1–D6, including allow-list and idempotency behavior.
## Out of Scope
New business calculations, direct repositories, or autonomous approval.
## Dependencies
TASK-22B-001; Stages 07, 10c, 14, 14b, 24.
## Relevant Files
Stage 22b Parts C/D; `docs/CHATBOT.md`; existing module APIs.
## References
ADR-129–131; Stage 22b business rules 1, 4, 6–8, 11–12.
## Implementation Requirements
Model gets no data/tools; composer post-checks every number/date; low confidence uses menu; bot order is pro forma unless audited allow-list.
## Acceptance Criteria
Generated reply numbers all occur in API DTO; duplicate webhook creates one order; model/API outage invents nothing.
## Tests Required
Property, injection, intent scoping, order idempotency, allow-list, availability AsAt, and outage tests.
## Edge Cases
Cross-company statement, unavailable POD, stale availability, and approval refusal.
## Security Considerations
Fresh OTP for sensitive documents and no model data access.
## Architectural Impact
Orchestrates existing module APIs only.
## Definition of Done
All six intents meet their specified contracts.
## Follow-up

# TASK-22B-003 — Implement document delivery and transport integration

## Status
NOT_STARTED
## Stage
Stage 22b
## Objective
Implement delivery tokens, webhook/email transport, templates, retry-to-human queue, consent, STOP, and rate limits.
## Why
Outbound documents and messages require expiry, audit, and channel safety.
## Scope
Build-list E1–E3 and F1, including signed webhook verification and metering counters.
## Out of Scope
Transport implementation from Stage 22.
## Dependencies
TASK-22B-002; Stage 22 transport; Stage 19 consent.
## Relevant Files
Stage 22b Infrastructure/API/Parts E/F; `docs/CHATBOT.md`, `docs/API_STANDARDS.md`.
## References
ADR-130, ADR-132; Stage 22b business rules 9–10.
## Implementation Requirements
Tokens are single-tenant, expiring, revocable, fetch-audited; WhatsApp outside window uses registered templates; STOP is immediate/permanent until re-granted.
## Acceptance Criteria
Delivery retry backs off to human queue; sensitive request/rate limits and consent checks work.
## Tests Required
Token, expiry/revocation, signature, rate-limit, retry, STOP, template, and metering tests.
## Edge Cases
Bounced notice, expired token, duplicate webhook, and provider outage.
## Security Considerations
No document without binding/OTP; no content in telemetry.
## Architectural Impact
Integrates transport while preserving assistant boundaries.
## Definition of Done
Delivery is auditable and safe with fake providers.
## Follow-up

# TASK-22B-004 — Complete conversational commerce verification

## Status
NOT_STARTED
## Stage
Stage 22b
## Objective
Complete permissions/OpenAPI/seed, POPIA documentation, migration, guards, and full no-provider test evidence.
## Why
The assistant is high-risk and must ship without external model or WhatsApp credentials.
## Scope
Build-list F2–F3 and Exit checklist.
## Out of Scope
Live provider credentials.
## Dependencies
TASK-22B-001 through TASK-22B-003.
## Relevant Files
Stage 22b Exit checklist; `docs/DATA_MODEL.md`, `docs/SECURITY.md`, `docs/PROGRESS.md`.
## References
`docs/AGENTS.md`, `docs/EXECUTION_STANDARD.md`.
## Implementation Requirements
Seed two bound contacts, one unbound, one locked; run conversation-safety, multi-company, licence-safety, migration, and full tests.
## Acceptance Criteria
All exit items have evidence; deferred POD is explicitly recorded if Stage 24 is absent.
## Tests Required
Full suite with no provider/credentials, migration Down, guards, OpenAPI, and seed.
## Edge Cases
Deferred dependency and provider outages.
## Security Considerations
POPIA consent, retention, transcript classification, and no content metering.
## Architectural Impact
Closes the conversational boundary.
## Definition of Done
Stage 22b is independently executable and verified.
## Follow-up

# TASK-30B-001 — Build control-plane device/licence APIs

## Status
NOT_STARTED
## Stage
Stage 30b
## Objective
Create the separate control-plane deployable, KMS/HSM signing, device API, vendor API, and licence lifecycle.
## Why
Vendor operations must issue and monitor licences without becoming a trading dependency.
## Scope
Device activation/rebind/lease/heartbeat/metering/diagnostics/update, vendor MFA/RBAC, issuance, grace, suspend/reactivate, rebind approval.
## Out of Scope
Tenant business data access and vendor revenue ledger.
## Dependencies
Stages 04b, 29, 30.
## Relevant Files
`docs/stages/STAGE-30b-control-plane.md`, `docs/API_CONTROL_PLANE.md`, `docs/LICENSING.md` §§6–9.
## References
ADR-024, ADR-025; Stage 30b first two deliverable groups.
## Implementation Requirements
Private key is unreadable by service; device operations are mTLS/idempotent/resumable; revoke is two-person and audited; payment failure never revokes live licence.
## Acceptance Criteria
Month-boundary issuance and activation/lease/heartbeat/metering operate against simulated installs.
## Tests Required
Device API, KMS boundary, issuance, payment-failure, MFA/RBAC, and audit tests.
## Edge Cases
Control-plane outage, rebind, lease expiry, and duplicate device requests.
## Security Considerations
MFA, IP allow-list, HSM, mTLS, and immutable audit.
## Architectural Impact
Separate deployable; must not be a trading dependency.
## Definition of Done
Device and licence control plane contracts are proven.
## Follow-up

# TASK-30B-002 — Implement metering, billing, dunning, and analytics

## Status
NOT_STARTED
## Stage
Stage 30b
## Objective
Ingest aggregate usage, calculate plans/overages, bill subscriptions, protect involuntary payments, and report health/adoption/revenue.
## Why
The vendor needs safe recurring billing and actionable fleet/customer analytics.
## Scope
Deduplicated rollups/backfill, limits, mandates/tokens, proration, dunning pause, reports, and partner isolation.
## Out of Scope
Tenant ledger postings.
## Dependencies
TASK-30B-001; Stage 21 payment abstraction.
## Relevant Files
Stage 30b Metering/Subscription billing; `docs/API_CONTROL_PLANE.md`, `docs/LICENSING.md`.
## References
ADR-025, ADR-029; Stage 30b business rules.
## Implementation Requirements
Accept whitelisted counts only; deduplicate `(nodeId, period)`; notify card/mandate expiry; pause dunning on undelivered notice; keep vendor books separate.
## Acceptance Criteria
500 nodes/90 days produce no gaps/duplicates; proration, downgrade fit, overage, dunning and partner isolation pass.
## Tests Required
Virtual-clock billing, rollup/backfill, delivery, partner security, and reporting tests.
## Edge Cases
Outage backfill, bounced email, fallback payment method, and read-only payment.
## Security Considerations
Never ingest customer content or card data.
## Architectural Impact
Vendor accounting is separate from tenant finance.
## Definition of Done
Billing and analytics satisfy the specified controls.
## Follow-up

# TASK-30B-003 — Implement abuse, fleet, support, provisioning, and vendor surfaces

## Status
NOT_STARTED
## Stage
Stage 30b
## Objective
Implement human-reviewed abuse queue, fleet operations, support grants, onboarding/offboarding, web console, Android vendor mode, and alerts.
## Why
Operations need safe intervention and visibility without automatic customer lockouts or data access.
## Scope
All remaining Stage 30b deliverables and APIs.
## Out of Scope
Tenant application code and autonomous disablement.
## Dependencies
TASK-30B-001, TASK-30B-002; Stage 30 Android.
## Relevant Files
Stage 30b Abuse/Fleet/Support/Provisioning/Console sections; `docs/API_CONTROL_PLANE.md` §§3–4.
## References
Stage 30b Tests / acceptance and Exit checklist.
## Implementation Requirements
Detect every listed signal including colliding series; human verdict/action only; support requires tenant approval/time-boxed grant; partner/fleet scopes are isolated.
## Acceptance Criteria
Cloned/snapshot/clock/impossible-travel/collision signals detect; legitimate replacement does not; outage has zero trading impact; grant expiry and dual audit work.
## Tests Required
Abuse, support security, fleet command, offline-day, offboarding deletion, console/API, Android, and audit tests.
## Edge Cases
False positive, vendor outage, expired grant, rollout halt, and offboarding retention.
## Security Considerations
No business data without grant; no auto-disable; immutable audit.
## Architectural Impact
Completes vendor operational boundary.
## Definition of Done
All Stage 30b exit items are evidenced.
## Follow-up

