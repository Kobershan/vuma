# STAGE 30 — Android Admin App

**Status:** NOT_STARTED — specification created 2026-09-12, implementation not certified · **Depends on:** 29, 02, 03, 04; 05 for approvals · **Reference reading:** [API standards](../API_STANDARDS.md), [reporting stage](STAGE-29-reporting-admin-dashboard-api.md), [security](../SECURITY.md) §§1–4; [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

## Objective

Deliver the tenant Android app for dashboards, stock lookup, approvals and alerts over cloud or authorized local endpoints, with a persisted offline cache and visible action status.

## What this stage does not own

The locked Android baseline is Kotlin/Compose under android/. The Flutter client under mobile/ already exists; preserve it as a separate prototype pending a superseding client-stack ADR. Vendor mode is Stage 30b with a separate identity/session and no tenant credentials.

## Deliverables

### Domain and client model

Kotlin `TenantSession`, `EndpointProfile`, `DashboardCacheEntity`, `PendingActionEntity`, `SyncStatusModel`. Start in `android/app/src/main/java/com/vuma/retail/mobile/`; map existing names before adding a new type. Reuse existing entities, value objects, immutable ledger records and versioned definitions instead of creating a parallel model.

### Application

API intents for approvals via Stage 05; client `QueueApprovalAction`, `RefreshDashboard`, `SwitchEndpointProfile`. Proposed Kotlin ports: `VumaApi`, `TenantSessionStore`, `OfflineActionRepository`. Adapt the existing `VumaApiClient` rather than adding a second transport. ViewModels expose cached freshness and queued status; the server owns authorization, financial effects and approval decisions.

### Infrastructure

Implement a durable Room/SQLite cache and action queue in the Android client, protected token storage, TLS endpoint validation and bounded background retry. Test local schema migrations and process restart. No new company database schema is assumed; any missing server command/status support is a prerequisite task in its owning module. Android sends scoped intents, not generic entity replicas.

### API

Consume `/api/v1/dashboard/overview`, `/api/v1/me/permissions`, authorized stock/approval endpoints and token refresh; declare missing server routes as dependencies before implementing screens. Publish OpenAPI examples, permissions, idempotency, concurrency and error codes before client implementation. Staff administration uses staff DTOs and authentication; loyalty/member credentials cannot access these routes. Preserve route compatibility where an endpoint exists already.

### Permissions and entitlement

Declare granular `mobile_admin.view`, `mobile_admin.manage` and distinct high-risk approval/posting/export permissions as applicable; do not grant broad administrator access to a mobile/member credential. Register the module manifest, enforce effective company access and module entitlements at the authority, and whitelist aggregate metering. Platform maintenance/read/export must retain the licensing carve-outs documented in the shared requirements.

## Business rules

1. Use OS-protected token storage, short-lived access tokens and rotating refresh; no embedded vendor/API secret or database connection.
2. Scope every cache and queue by tenant/user/company; account or endpoint switching clears bearer credentials and prevents reuse against another host.
3. An offline approval is a pending intent, not a completed approval. Revalidate permission, version and expiry on reconnect.
4. Cloud connectivity loss permits cached reads; local profile uses validated TLS and discovery/explicit enrollment, never a phone's localhost default.
5. Push carries an opaque event reference; fetch sensitive content through authenticated APIs.
6. Expired/revoked sessions preserve safely queued work but require reauthentication; cancellation and rejection remain visible.
7. Apply the shared requirements in §§1–5 to every entry point; authorization happens again when a queued intent executes.

## Parts — the build list

- [ ] 30-P01: Implement endpoint enrollment, secure login/refresh and tenant-scoped Room storage.
- [ ] 30-P02: Deliver dashboard/stock/approval flows with persistent intent queue and freshness labels.
- [ ] 30-P03: Add push, accessibility, release signing and physical-device outage/security tests.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Tests / acceptance

- `Airplane_mode_preserves_dashboard`: after one successful sync and app restart offline, cached data renders with last-synced time.
- `Revoked_approval_is_refused_on_reconnect`: queue offline, revoke permission, reconnect; server rejects and UI never labels it approved.
- `Endpoint_switch_drops_old_token`: move from enrolled server A to B; no Authorization header from A is sent to B.
- `Ten_retries_one_approval`: retry the same action ten times; one authoritative transition.
- `Published_app_contains_no_vendor_secret`: inspect signed release and configuration assets; no service private keys or Orbit service credentials.
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
