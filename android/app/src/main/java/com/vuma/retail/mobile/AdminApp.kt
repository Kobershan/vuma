package com.vuma.retail.mobile

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun VumaAdminApp(application: VumaApplication) {
    val owner = androidx.compose.ui.platform.LocalLifecycleOwner.current
    var session by remember { mutableStateOf<TenantSession?>(null) }
    var dashboard by remember { mutableStateOf<DashboardSnapshot?>(null) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }

    if (session == null) {
        LoginScreen(busy, error) { endpoint, tenant, username, password ->
            busy = true; error = null
            owner.lifecycleScope.launch {
                try {
                    val result = withContext(Dispatchers.IO) {
                        val profile = EndpointProfile.enroll(endpoint, tenant)
                        val api = VumaApiClient(profile.baseUrl, "")
                        val token = api.signIn(username, password)
                        application.sessionStore.saveRefreshToken(token.refreshToken)
                        TenantSession(profile, token.userId, emptySet(), token.accessToken) to api.dashboardOverview()
                    }
                    session = result.first; dashboard = result.second
                } catch (cause: Exception) { error = cause.message ?: "Sign in failed." }
                finally { busy = false }
            }
        }
    } else {
        DashboardScreen(dashboard, busy, error) {
            busy = true; error = null
            owner.lifecycleScope.launch {
                try { dashboard = withContext(Dispatchers.IO) { VumaApiClient(session!!.profile.baseUrl, session!!.accessToken).dashboardOverview() } }
                catch (cause: Exception) { error = cause.message ?: "Refresh failed." }
                finally { busy = false }
            }
        }
    }
}

@Composable
private fun LoginScreen(busy: Boolean, error: String?, onLogin: (String, String, String, String) -> Unit) {
    var endpoint by remember { mutableStateOf("") }
    var tenant by remember { mutableStateOf("") }
    var username by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Vuma admin", style = MaterialTheme.typography.headlineLarge)
        Text("Sign in to an enrolled tenant endpoint.")
        OutlinedTextField(endpoint, { endpoint = it }, Modifier.fillMaxWidth(), label = { Text("HTTPS API endpoint") })
        OutlinedTextField(tenant, { tenant = it }, Modifier.fillMaxWidth(), label = { Text("Tenant ID") })
        OutlinedTextField(username, { username = it }, Modifier.fillMaxWidth(), label = { Text("Username") })
        OutlinedTextField(password, { password = it }, Modifier.fillMaxWidth(), label = { Text("Password") }, visualTransformation = PasswordVisualTransformation())
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        Button(onClick = { onLogin(endpoint, tenant, username, password) }, enabled = !busy && endpoint.isNotBlank() && tenant.isNotBlank() && username.isNotBlank() && password.isNotBlank()) {
            if (busy) CircularProgressIndicator() else Text("Sign in")
        }
    }
}

@Composable
private fun DashboardScreen(snapshot: DashboardSnapshot?, busy: Boolean, error: String?, onRefresh: () -> Unit) {
    Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Column { Text("Operations overview", style = MaterialTheme.typography.headlineMedium); Text(snapshot?.asAt ?: "No live snapshot") }
            Button(onClick = onRefresh, enabled = !busy) { if (busy) CircularProgressIndicator() else Text("Refresh") }
        }
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        snapshot?.let {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                MetricCard("Sales today", it.salesByCurrency.entries.joinToString { (currency, amount) -> "$currency ${"%.2f".format(amount)}" }, Modifier.weight(1f))
                MetricCard("Orders", it.ordersToday.toString(), Modifier.weight(1f))
            }
            MetricCard("Open orders", it.openOrders.toString(), Modifier.fillMaxWidth())
        }
    }
}

@Composable
private fun MetricCard(label: String, value: String, modifier: Modifier) {
    Card(modifier) { Column(Modifier.padding(16.dp)) { Text(label); Text(value, style = MaterialTheme.typography.titleLarge) } }
}
