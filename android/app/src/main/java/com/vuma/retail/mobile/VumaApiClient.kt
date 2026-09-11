package com.vuma.retail.mobile

import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject

/** Small authenticated client for the existing versioned Vuma API. Tokens are supplied in memory. */
class VumaApiClient(baseUrl: String, private val accessToken: String, private val http: OkHttpClient = OkHttpClient()) {
    private val base = baseUrl.trimEnd('/').toHttpUrl()

    fun operator(): OperatorSnapshot {
        val url = base.newBuilder().addPathSegments("api/v1/operator/").build()
        val request = Request.Builder().url(url).header("Authorization", "Bearer $accessToken").get().build()
        http.newCall(request).execute().use { response ->
            if (!response.isSuccessful) error("Vuma API returned HTTP ${response.code}")
            val body = response.body?.string() ?: error("Vuma API returned an empty response")
            val json = JSONObject(body)
            return OperatorSnapshot(json.optString("displayName"), json.optBoolean("isActive"), json.optJSONArray("companies")?.length() ?: 0)
        }
    }
}

data class OperatorSnapshot(val displayName: String, val active: Boolean, val companyCount: Int)
