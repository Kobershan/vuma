package com.vuma.retail.mobile.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val Light = lightColorScheme(primary = Color(0xFF0B7A5A), secondary = Color(0xFF1B5FB0), background = Color(0xFFFAFAFA))
private val Dark = darkColorScheme(primary = Color(0xFF16A97D), secondary = Color(0xFF4C94F0), background = Color.Black)

@Composable fun VumaTheme(darkTheme: Boolean = false, content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (darkTheme) Dark else Light, content = content)
}
