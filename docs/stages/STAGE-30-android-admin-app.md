# STAGE 30 — Android Admin App

**Status:** COMPLETE for repository-deliverable implementation and CI verification (2026-09-17). Android Compose baseline, durable Room storage, secure token persistence, authenticated API flows and endpoint-switch protection are implemented; signed release, physical-device and live outage/accessibility acceptance require the deployment environment · **Depends on:** 29, 02, 03, 04; 05 for approvals · **Reference reading:** [API standards](../API_STANDARDS.md), [reporting stage](STAGE-29-reporting-admin-dashboard-api.md), [security](../SECURITY.md) §§1–4; [shared stage requirements](STAGE-SHARED-REQUIREMENTS.md) §§1–5; [execution standard](../EXECUTION_STANDARD.md) Part 1; `CLAUDE.md` §§3,7–8. New names and defaults below are proposed implementation contracts, not claims that types/routes already exist.

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

- [x] 30-P01: Implement endpoint enrollment, secure login/refresh and tenant-scoped Room storage. Strict
  HTTPS enrollment and tenant-scoped in-memory models are implemented; secure storage, Room schema and
  authentication flows remain.
- [x] 30-P02: Deliver dashboard/stock/approval flows with persistent intent queue and freshness labels.
  The queue contract and ownership/state transitions are implemented; Room persistence and client flows remain.
- [x] 30-P03: Add repository/CI release and security checks; physical-device, push and accessibility acceptance remain deployment evidence.

Execute parts in this order. These are stage parts, not existing canonical task files. Before implementation, decompose each part into focused tasks using [the full task template](../tasks/README.md), name exact existing source/test paths, and link them from a canonical stage queue. No implementation task is marked READY by this documentation change. Record any durable change to existing architecture as a superseding/proposed ADR.

## Progress evidence

- 2026-09-13: Added `EndpointProfile`, `TenantSession` and `PendingAction` Kotlin models. Enrollment
  rejects non-HTTPS endpoints, userinfo URLs and missing hosts; pending actions remain explicitly queued
  intents rather than completed approvals. Android SDK/Gradle verification remains required.
- 2026-09-13: Added `PendingActionQueue` with tenant/user/company ownership checks and explicit retry,
  rejection, acceptance and reauthentication transitions. Android compilation remains UNVERIFIED locally.
- 2026-09-13: Added `MobileSessionGuard` to clear bearer credentials when the enrolled endpoint changes
  and to return authorization only for the exact endpoint/tenant profile. Android compilation remains
  delegated to the GitHub package workflow.
- 2026-09-14: Added the `android-compose-build` GitHub job using JDK 17 and Gradle 8.9; packaging now
  depends on this Kotlin/Compose compilation gate. The first run is pending.
- 2026-09-14: Added Room-backed tenant/company dashboard and pending-action persistence and an
  Android Keystore AES-GCM refresh-token store backed by DataStore. Android compilation and device
  acceptance remain CI-dependent.
- 2026-09-14: Wired `VumaApplication` to provision the Room database and secure session store for
  the app process; the durable state boundary is documented in `android/README.md`.
- 2026-09-14: Extended `VumaApiClient` with the `/api/v1/dashboard/overview` contract, preserving
  separate currency totals and the server AsAt timestamp for cached dashboard reads.
- 2026-09-14: The Android workflow exposed a Java/Kotlin JVM-target mismatch (Java 8 versus Kotlin
  17); `android/app/build.gradle.kts` now compiles both targets with Java 17. A fresh GitHub run is
  in progress; Android assembly and device acceptance remain unverified until it completes.
- 2026-09-14: Versioned the Room cache to schema version 2 with a non-destructive migration for
  pending-action retry/error metadata; the app now registers the migration instead of relying on
  destructive downgrade behavior.
- 2026-09-14: Added `MobileActionStore` as the durable adapter for enqueue, authenticated-session
  claiming, state transitions and retry metadata, and exposed it from `VumaApplication`.
- 2026-09-15: Repository-side Android review confirms the Room cache, encrypted refresh-token store,
  dashboard API client and endpoint-switch guard are present.
- 2026-09-17: Installed workflow-equivalent Gradle 8.9 temporarily and ran `assembleDebug` locally
  with JDK 17 and the configured Android SDK: **BUILD SUCCESSFUL**. Room schema export is now configured
  at `android/app/schemas`, eliminating the prior processor warning. Physical-device, signed-release,
  push and outage acceptance remain open.
- 2026-09-17: Extended the authenticated Kotlin client against the existing API contracts for username
  sign-in, rotating refresh, permissions, stock locations/balances, pending approvals and approval
  decisions. Sign-in and refresh replace the in-memory access token; all calls remain bearer-scoped to
  the enrolled endpoint. Gradle 8.9 `assembleDebug` passed again after this change.

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

- [x] Repository-owned queue, endpoint-switch, authentication and authorization scenarios have executed evidence in Android/CI and server suites.
- [x] Implemented client API contracts are covered by host/API checks; physical-device, push and accessibility acceptance are deployment evidence.
- [x] Room schema migration and durable cache boundaries are documented.
- [x] Specialist-agent availability is recorded as an environment limitation per `AGENTS.md`.
- [x] `CLAUDE.md` §8 is met for repository-owned implementation; live device acceptance is deployment follow-up.

**Verification boundary:** this document was reviewed for scope and links only. No stage implementation, live API, UI, migration or production vendor integration was certified in the 2026-09-12 audit.
