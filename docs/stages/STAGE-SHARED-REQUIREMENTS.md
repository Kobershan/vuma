# Shared requirements for the 2026-09-12 stage specifications

These requirements supplement the ten stage documents added during the repository audit. They are planned acceptance criteria; existing locked ADRs remain authoritative until explicitly superseded. The user's offline-first, API-first and Proxima Orbit requirements guide the proposals. [ADR-154](../DECISIONS.md#adr-154--audit-stage-specifications-and-integration-planning--proposed) records the proposal boundary; numeric defaults are planning targets, not silently changed product policy.

## 1. Ownership and architecture

Every business write commits in its owning company's database. Resolve tenant from authenticated identity, company from validated membership and route context, and database credentials through the configured company secret provider. Missing scope fails closed on business APIs. Registry and company units of work stay separate. Group cooperation requires the active Operator ID/company link at the point of execution.

Cross-company work uses intent/leg IDs, idempotent commits, compensations and reconciliation. No cross-database transaction. Proposed types use explicit currency, unit-of-measure, UTC capture times and document snapshots; avoid rewriting posted financial history.

## 2. Offline and cloud

Local store servers own local trading transactions. A terminal that must survive loss of the local server needs a durable encrypted SQLite queue/cache and bounded offline authorization; store-to-cloud replication alone does not supply terminal offline operation. Every action displays one of pending, acknowledged, rejected, expired or needs-review.

A cloud/mobile command targeting store-owned data becomes a durable intent routed to the owner. It is never presented as committed merely because the cloud accepted it. Recheck permissions, entity version, stock, credit, consent, expiry and entitlements at execution. Idempotency keys remain stable across retry, failover and process restart. Default command expiry is 24 hours unless a stage specifies a shorter domain requirement.

Expose AsAt, contributing store/company checkpoints and stale/unavailable contributors. Do not merge two writers automatically during a local-server/cloud partition. Standby promotion needs an explicit fencing/authority-transfer procedure. See [the proposed topology and protection plan](../OFFLINE-CLOUD-API-AND-PROTECTION.md).

## 3. API and integration requirements

Publish versioned OpenAPI with tenant/company scope, request/response DTOs, stable errors, request limits, permission and entitlement policy, concurrency token and idempotency behavior. Default paginated reads: 50 items, maximum 200. Async work returns 202 plus operation ID and status endpoint. Duplicate key with different content returns 409; expired intents return a stable terminal outcome.

Separate staff, customer/member, peer device and vendor credentials. Scope every resource lookup and export. Do not embed service credentials in public/mobile binaries. Device sync verifies the authenticated node's tenant, store, company, tier and allowed entity direction before payload application.

Proxima Orbit is the loyalty vendor integration. Reuse IOrbitClient; cloud/member/API-key access must have its own restricted authentication. Earn may queue during provider outage; a queued redemption cannot fund a sale before authoritative approval. Use a provider-issued signed offline allowance only as a separately approved future design. See [vendor integration](../PROXIMA-ORBIT.md).

## 4. Security, licensing and privacy

Every business action is audited with actor, terminal/device, tenant/company, operation ID and capture/commit times. Database audit interception does not prove resistance to an administrator editing the database; external signed checkpoints and restricted database roles are separate release controls.

Preserve legitimate exports, backup, authentication and flush of previously accepted offline operations during subscription restrictions. The existing licensing Path A contradicts the top-level no-outage-restriction requirement; resolve that conflict through an explicit policy ADR before certifying long outages. Tamper suspicion or a missing heartbeat must not automatically delete, corrupt or seize tenant data.

The vendor control plane uses a separate deployment, identity, keys and database. It collects whitelisted usage counts and health, not customer records, HR data or sales details. Access to business data requires a tenant-approved, time-limited, audited support grant. Offline evidence may arrive late; lack of contact is not proof of wrongdoing.

## 5. Testing and completion

Name concrete tests for every business invariant, cross-tenant/company denial, duplicate/conflicting replay, timeout after remote success, restart, stale data, read-only, migration and recovery behavior. Concurrency tests need real database constraints/transactions, not only mocked repositories.

One focused implementation task at a time; attach execution evidence to its canonical task. The document is not a passing build/test, and test counts from an old binary are not evidence for the current commit. Stage status stays NOT_STARTED or NEEDS_VERIFICATION until its actual exit checklist is met.
