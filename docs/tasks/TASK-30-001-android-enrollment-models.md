# TASK-30-001 — Android endpoint enrollment and session models

**Status:** COMPLETE for model/validation slice · **Stage:** 30 · **Type:** Android client, security

Added strict HTTPS `EndpointProfile` enrollment and tenant/user/company-scoped `TenantSession` and
`PendingAction` models. The profile excludes URL userinfo and bearer credentials; pending actions
represent server intents and cannot be mistaken for completed approvals.

Evidence: source added under `android/app/src/main/java/com/vuma/retail/mobile/`. Android SDK/Gradle
is unavailable in this environment, so assemble and device tests remain UNVERIFIED. Room persistence,
protected token storage, refresh, retry, and endpoint-switch invalidation are now implemented in the
Android baseline; authenticated API/device acceptance remains open.
