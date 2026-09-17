# TASK-30-001 — Android endpoint enrollment and session models

**Status:** COMPLETE for model/validation slice · **Stage:** 30 · **Type:** Android client, security

Added strict HTTPS `EndpointProfile` enrollment and tenant/user/company-scoped `TenantSession` and
`PendingAction` models. The profile excludes URL userinfo and bearer credentials; pending actions
represent server intents and cannot be mistaken for completed approvals.

Evidence: source added under `android/app/src/main/java/com/vuma/retail/mobile/`. Room persistence,
protected token storage, rotating refresh, retry, endpoint-switch invalidation and authenticated API
session replacement are implemented in the Android baseline. Android `assembleDebug` passed locally
with Gradle 8.9/JDK 17 on 2026-09-17; device acceptance remains open.
