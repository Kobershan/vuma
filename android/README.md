# Vuma mobile app

The Android app is a Kotlin/Compose companion for the Vuma API. It uses the same green-confirmation
visual language and touch spacing as the desktop design system, and calls the existing authenticated
`/api/v1` contracts rather than duplicating business logic.

Build on a machine with Android SDK and Gradle installed:

```shell
gradle -p android :app:assembleDebug
```

The access token is intentionally supplied to `VumaApiClient` in memory; do not persist bearer tokens
in plain preferences or browser-style local storage. Production sign-in should bind the client to the
existing JWT/refresh-token policy before issuing an installable release.
