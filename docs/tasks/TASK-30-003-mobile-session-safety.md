# TASK-30-003 — Mobile endpoint/session safety

**Status:** COMPLETE for session-binding slice · **Stage:** 30 · **Type:** Android client security

Added `MobileSessionGuard`, which binds bearer credentials to the exact enrolled endpoint and tenant,
clears credentials on endpoint switching or invalidation, and leaves queued work available only for a
later authenticated session. Tokens are never stored in `EndpointProfile`.

Evidence: Kotlin source is under `android/app/src/main/java/com/vuma/retail/mobile/`. Workflow-equivalent
Gradle 8.9/JDK 17 compilation passes locally with `assembleDebug`; signed release and physical-device
verification remain deployment acceptance items.
