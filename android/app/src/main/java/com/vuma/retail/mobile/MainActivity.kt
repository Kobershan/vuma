package com.vuma.retail.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.vuma.retail.mobile.ui.theme.VumaTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { VumaTheme { VumaDashboard() } }
    }
}

@Composable
private fun VumaDashboard() {
    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(modifier = Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Text("vuma", style = MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold)
            Text("Operations overview", style = MaterialTheme.typography.titleLarge)
            Text("Good morning, Aisha", color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                MobileStat("Sales today", "R 48,260", "↑ 12.4%", Modifier.weight(1f))
                MobileStat("Orders", "126", "18 awaiting", Modifier.weight(1f))
            }
            MobileStat("Inventory health", "82% healthy", "7 items below reorder level", Modifier.fillMaxWidth())
            Spacer(Modifier.height(4.dp))
            Text("Needs attention", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            AlertRow("Low stock", "7 items below reorder level")
            AlertRow("Orders delayed", "4 orders need a dispatch update")
            AlertRow("Transfer awaiting receipt", "TR-2026-0041 from Cape Town")
        }
    }
}

@Composable
private fun MobileStat(label: String, value: String, detail: String, modifier: Modifier) {
    Card(modifier = modifier, shape = RoundedCornerShape(16.dp), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)) {
        Column(modifier = Modifier.padding(16.dp)) { Text(label, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant); Text(value, fontSize = 24.sp, fontWeight = FontWeight.SemiBold); Text(detail, fontSize = 12.sp, color = MaterialTheme.colorScheme.primary) }
    }
}

@Composable
private fun AlertRow(title: String, detail: String) {
    Card(modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(12.dp), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(modifier = Modifier.padding(16.dp)) { Text(title, fontWeight = FontWeight.SemiBold); Text(detail, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant) }
    }
}
