// Generated from design/tokens.json. DO NOT EDIT BY HAND.
// Regenerate with: pwsh scripts/generate-tokens.ps1
package com.vuma.core.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

object VumaColorTokens {
    // Surface
    val surfaceBase = Color(0xFF000000)
    val surfaceRaised = Color(0xFF141416)
    val surfaceSunken = Color(0xFF0A0A0B)

    // Text
    val textPrimary = Color(0xFFF5F5F7)
    val textSecondary = Color(0xFFA0A0A8)
    val textTertiary = Color(0xFFA8A8B0)

    // Accent
    val accentPrimary = Color(0xFF16A97D)
    val accentQuiet = Color(0xFF0E2A22)

    // Semantic
    val positive = Color(0xFF16A97D)
    val warning = Color(0xFFE5940A)
    val critical = Color(0xFFFF5A4E)
    val info = Color(0xFF4C94F0)

    val separator = Color(0xFF2A2A2E)
}

object VumaTypographyTokens {
    val display: FontFamily = FontFamily.Default
    val displaySize: androidx.compose.ui.unit.TextUnit = 44.sp
    val displayWeight: FontWeight = FontWeight(600)
    val displayLineHeight: androidx.compose.ui.unit.TextUnit = 48.sp
    val title1: FontFamily = FontFamily.Default
    val title1Size: androidx.compose.ui.unit.TextUnit = 30.sp
    val title1Weight: FontWeight = FontWeight(600)
    val title1LineHeight: androidx.compose.ui.unit.TextUnit = 36.sp
    val title2: FontFamily = FontFamily.Default
    val title2Size: androidx.compose.ui.unit.TextUnit = 22.sp
    val title2Weight: FontWeight = FontWeight(600)
    val title2LineHeight: androidx.compose.ui.unit.TextUnit = 28.sp
    val headline: FontFamily = FontFamily.Default
    val headlineSize: androidx.compose.ui.unit.TextUnit = 17.sp
    val headlineWeight: FontWeight = FontWeight(600)
    val headlineLineHeight: androidx.compose.ui.unit.TextUnit = 22.sp
    val body: FontFamily = FontFamily.Default
    val bodySize: androidx.compose.ui.unit.TextUnit = 15.sp
    val bodyWeight: FontWeight = FontWeight(400)
    val bodyLineHeight: androidx.compose.ui.unit.TextUnit = 21.sp
    val callout: FontFamily = FontFamily.Default
    val calloutSize: androidx.compose.ui.unit.TextUnit = 14.sp
    val calloutWeight: FontWeight = FontWeight(400)
    val calloutLineHeight: androidx.compose.ui.unit.TextUnit = 19.sp
    val caption: FontFamily = FontFamily.Default
    val captionSize: androidx.compose.ui.unit.TextUnit = 12.sp
    val captionWeight: FontWeight = FontWeight(500)
    val captionLineHeight: androidx.compose.ui.unit.TextUnit = 16.sp
    val mono: FontFamily = FontFamily.Default
    val monoSize: androidx.compose.ui.unit.TextUnit = 13.sp
    val monoWeight: FontWeight = FontWeight(400)
    val monoLineHeight: androidx.compose.ui.unit.TextUnit = 18.sp
}

object VumaShapeTokens {
    val sm = androidx.compose.foundation.shape.RoundedCornerShape(8.dp)
    val md = androidx.compose.foundation.shape.RoundedCornerShape(12.dp)
    val lg = androidx.compose.foundation.shape.RoundedCornerShape(16.dp)
    val xl = androidx.compose.foundation.shape.RoundedCornerShape(22.dp)
    val pill = androidx.compose.foundation.shape.RoundedCornerShape(999.dp)
}

object VumaSpacingTokens {
    val spacing4 = 4.dp
    val spacing8 = 8.dp
    val spacing12 = 12.dp
    val spacing16 = 16.dp
    val spacing24 = 24.dp
    val spacing32 = 32.dp
    val spacing48 = 48.dp
    val spacing64 = 64.dp
}

object VumaMotionTokens {
    const val instantDurationMs = 100
    const val instantCurve = "ease-out"
    const val vumatickDurationMs = 220
    const val vumatickCurve = "ease-out"
    const val quickDurationMs = 180
    const val quickCurve = "cubic-bezier(.2,0,0,1)"
    const val standardDurationMs = 260
    const val standardCurve = "cubic-bezier(.2,0,0,1)"
    const val vumaTickDurationMs = 220
    const val vumaTickCurve = "cubic-bezier(.65,0,.35,1)"
}

object VumaTouchTargetTokens {
    val posPrimarySize = 64.dp
    val posSecondarySize = 48.dp
    val backOfficeSize = 36.dp
    val androidWarehouseSize = 56.dp
}
