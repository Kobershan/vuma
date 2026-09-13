package com.vuma.retail.mobile

import java.net.URI

/** An explicitly enrolled Vuma API endpoint; bearer tokens are never part of this profile. */
data class EndpointProfile(
    val baseUrl: String,
    val tenantId: String,
) {
    init {
        require(tenantId.isNotBlank()) { "tenantId is required" }
        val uri = URI(baseUrl)
        require(uri.scheme.equals("https", ignoreCase = true)) { "Vuma endpoints must use HTTPS" }
        require(!uri.host.isNullOrBlank()) { "Vuma endpoint host is required" }
        require(uri.userInfo == null) { "Vuma endpoint user information is not allowed" }
    }

    companion object {
        fun enroll(rawUrl: String, tenantId: String): EndpointProfile {
            val normalized = rawUrl.trim().trimEnd('/')
            require(normalized.isNotBlank()) { "Endpoint URL is required" }
            return EndpointProfile(normalized, tenantId.trim())
        }
    }
}

/** Session scope kept in memory by the client; persistent token storage belongs to the secure store. */
data class TenantSession(
    val profile: EndpointProfile,
    val userId: String,
    val companyIds: Set<String>,
    val accessToken: String,
)

enum class PendingActionState { Queued, Sending, Accepted, Rejected, RequiresReauthentication }

/** An offline intent, never a claim that the server-side business transition already happened. */
data class PendingAction(
    val id: String,
    val tenantId: String,
    val userId: String,
    val companyId: String,
    val operation: String,
    val payloadJson: String,
    val state: PendingActionState = PendingActionState.Queued,
)
