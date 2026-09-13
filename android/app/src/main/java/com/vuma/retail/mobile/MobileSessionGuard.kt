package com.vuma.retail.mobile

/**
 * Keeps bearer credentials bound to the enrolled endpoint and tenant. A profile switch deliberately
 * clears the session; queued intents remain in storage but can only be claimed after reauthentication.
 */
class MobileSessionGuard {
    private var session: TenantSession? = null

    fun authenticate(newSession: TenantSession) {
        require(newSession.accessToken.isNotBlank()) { "Access token is required" }
        session = newSession
    }

    fun current(): TenantSession? = session

    /** Clears credentials before an endpoint or tenant changes. */
    fun switchEndpoint(profile: EndpointProfile) {
        if (session?.profile != profile) {
            session = null
        }
    }

    fun invalidate() {
        session = null
    }

    /** Returns a bearer token only for the exact enrolled endpoint and tenant. */
    fun authorizationFor(profile: EndpointProfile): String? =
        session?.takeIf { it.profile == profile }?.accessToken
}
