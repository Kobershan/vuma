package com.vuma.retail.mobile

import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject

/** Small authenticated client for the existing versioned Vuma API. Tokens are supplied in memory. */
class VumaApiClient(baseUrl: String, accessToken: String, private val http: OkHttpClient = OkHttpClient()) {
    private val base = baseUrl.trimEnd('/').toHttpUrl()
    var accessToken: String = accessToken
        private set

    fun signIn(userName: String, password: String, storeId: String? = null): MobileToken {
        val payload = JSONObject().put("userName", userName).put("password", password)
        if (storeId != null) payload.put("storeId", storeId)
        val token = postJson("api/v1/auth/token", payload).toToken()
        accessToken = token.accessToken
        return token
    }

    fun refresh(refreshToken: String): MobileToken {
        val token = postJson("api/v1/auth/refresh", JSONObject().put("refreshToken", refreshToken)).toToken()
        accessToken = token.accessToken
        return token
    }

    fun permissions(): Set<String> {
        val json = getJson("api/v1/me/permissions")
        val values = json.optJSONArray("permissions") ?: return emptySet()
        return buildSet {
            for (index in 0 until values.length()) add(values.getString(index))
        }
    }

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

    fun dashboardOverview(): DashboardSnapshot {
        val json = getJson("api/v1/dashboard/overview")
        val sales = json.optJSONArray("salesByCurrency")
        val salesByCurrency = buildMap {
            if (sales != null) for (index in 0 until sales.length()) {
                val row = sales.getJSONObject(index)
                put(row.getString("currency"), row.getDouble("amount"))
            }
        }
        return DashboardSnapshot(json.optDouble("salesToday", 0.0), salesByCurrency,
            json.optInt("ordersToday"), json.optInt("openOrders"), json.optString("asAt"))
    }

    fun stockLocations(): List<StockLocation> = getArray("api/v1/inventory/locations/")
        .objects().map { StockLocation(it.getString("id"), it.getString("code"), it.getString("name"), it.getString("type"), it.optBoolean("isActive")) }

    fun stockBalances(locationId: String): List<StockBalance> = getArray("api/v1/inventory/locations/$locationId/balances")
        .objects().map { StockBalance(it.getString("locationId"), it.optStringOrNull("itemId"), it.optStringOrNull("itemVariantId"), it.getDouble("quantityOnHand"), it.getString("unitOfMeasure"), it.getDouble("totalValue"), it.getString("currency")) }

    fun pendingApprovals(storeId: String? = null): List<ApprovalSummary> {
        return getArray("api/v1/workflow/approvals/", storeId?.let { mapOf("storeId" to it) } ?: emptyMap()).objects().map {
            ApprovalSummary(it.getString("id"), it.getString("module"), it.getString("entityType"), it.getString("action"), it.getString("subjectEntityId"), it.getString("status"))
        }
    }

    fun decideApproval(requestId: String, outcome: String, comment: String? = null): ApprovalDecision {
        val payload = JSONObject().put("outcome", outcome)
        if (comment != null) payload.put("comment", comment)
        val json = postJson("api/v1/workflow/approvals/$requestId/decide", payload)
        return ApprovalDecision(json.getString("requestId"), json.getString("status"), json.getInt("approvalCount"), json.getInt("minApprovals"))
    }

    private fun getJson(path: String): JSONObject {
        return JSONObject(execute(path, emptyMap(), false))
    }

    private fun getArray(path: String, query: Map<String, String> = emptyMap()): JSONArray {
        return JSONArray(execute(path, query, false))
    }

    private fun postJson(path: String, payload: JSONObject): JSONObject {
        return JSONObject(execute(path, emptyMap(), true, payload))
    }

    private fun execute(path: String, query: Map<String, String>, post: Boolean, payload: JSONObject? = null): String {
        val url = base.newBuilder().addPathSegments(path).apply {
            query.forEach { (key, value) -> addQueryParameter(key, value) }
        }.build()
        val builder = Request.Builder().url(url).header("Authorization", "Bearer $accessToken")
        val request = if (post) builder.post((payload ?: JSONObject()).toString().toRequestBody("application/json".toMediaType())).build() else builder.get().build()
        http.newCall(request).execute().use { response ->
            if (!response.isSuccessful) error("Vuma API returned HTTP ${response.code}")
            return response.body?.string() ?: error("Vuma API returned an empty response")
        }
    }

    private fun JSONObject.toToken() = MobileToken(
        getString("accessToken"), getString("refreshToken"), getString("expiresAt"),
        getString("userId"), getString("displayName"))

}

private fun JSONArray.objects(): List<JSONObject> = (0 until length()).map { getJSONObject(it) }

private fun JSONObject.optStringOrNull(name: String): String? = if (isNull(name)) null else optString(name).takeIf { it.isNotBlank() }

data class MobileToken(val accessToken: String, val refreshToken: String, val expiresAt: String, val userId: String, val displayName: String)
data class StockLocation(val id: String, val code: String, val name: String, val type: String, val active: Boolean)
data class StockBalance(val locationId: String, val itemId: String?, val itemVariantId: String?, val quantityOnHand: Double, val unitOfMeasure: String, val totalValue: Double, val currency: String)
data class ApprovalSummary(val id: String, val module: String, val entityType: String, val action: String, val subjectEntityId: String, val status: String)
data class ApprovalDecision(val requestId: String, val status: String, val approvalCount: Int, val minApprovals: Int)

data class OperatorSnapshot(val displayName: String, val active: Boolean, val companyCount: Int)

data class DashboardSnapshot(
    val salesToday: Double,
    val salesByCurrency: Map<String, Double>,
    val ordersToday: Int,
    val openOrders: Int,
    val asAt: String,
)
