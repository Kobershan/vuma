package com.vuma.core.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.shape.RoundedCornerShape

// Generated from design/tokens.json. DO NOT EDIT BY HAND.
// Regenerate with: pwsh scripts/generate-tokens.ps1

object VumaColorTokens {
    // Surface
    val surfaceBase = Color(0xFFFAFAFA)
    val surfaceRaised = Color(0xFFFFFFFF)
    val surfaceSunken = Color(0xFFF0F0F2)

    // Text
    val textPrimary = Color(0xFF0A0A0B)
    val textSecondary = Color(0xFF5A5A62)
    val textTertiary = Color(0xFF8E8E96)

    // Accent
    val accentPrimary = Color(0xFF0B7A5A)
    val accentQuiet = Color(0xFFE6F3EE)

    // Semantic
    val positive = Color(0xFF0B7A5A)
    val warning = Color(0xFFB26A00)
    val critical = Color(0xFFC0261F)
    val info = Color(0xFF1B5FB0)

    val separator = Color(0xFFE3E3E6)
}

object VumaTypographyTokens {
    val display: FontFamily = FontFamily("Inter Display")
    val title1: FontFamily = FontFamily("Inter Display")
    val title2: FontFamily = FontFamily("Inter Display")
    val headline: FontFamily = FontFamily("Inter")
    val body: FontFamily = FontFamily("Inter")
    val callout: FontFamily = FontFamily("Inter")
    val caption: FontFamily = FontFamily("Inter")
    val mono: FontFamily = FontFamily("JetBrains Mono")

    val displaySize: Sp = 44.sp
    val title1Size: Sp = 30.sp
    val title2Size: Sp = 22.sp
    val headlineSize: Sp = 17.sp
    val bodySize: Sp = 15.sp
    val calloutSize: Sp = 14.sp
    val captionSize: Sp = 12.sp
    val monoSize: Sp = 13.sp

    val displayWeight: FontWeight = FontWeight(600)
    val title1Weight: FontWeight = FontWeight(600)
    val title2Weight: FontWeight = FontWeight(600)
    val headlineWeight: FontWeight = FontWeight(600)
    val bodyWeight: FontWeight = FontWeight(400)
    val calloutWeight: FontWeight = FontWeight(400)
    val captionWeight: FontWeight = FontWeight(500)
    val monoWeight: FontWeight = FontWeight(400)

    val displayLineHeight: Sp = 48.sp
    val title1LineHeight: Sp = 36.sp
    val title2LineHeight: Sp = 28.sp
    val headlineLineHeight: Sp = 22.sp
    val bodyLineHeight: Sp = 21.sp
    val calloutLineHeight: Sp = 19.sp
    val captionLineHeight: Sp = 16.sp
    val monoLineHeight: Sp = 18.sp
}

object VumaShapeTokens {
    val sm = RoundedCornerShape(8.dp)
    val md = RoundedCornerShape(12.dp)
    val lg = RoundedCornerShape(16.dp)
    val xl = RoundedCornerShape(22.dp)
    val pill = RoundedCornerShape(999.dp)
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
    val instantDurationMs = 100
    val instantCurve = androidx.compose.animation.core.CurveSpec("ease-out")
    val quickDurationMs = 180
    val quickCurve = androidx.compose.animation.core.CurveSpec("cubic-bezier(.2,0,0,1)")
    val standardDurationMs = 260
    val standardCurve = androidx.compose.animation.core.CurveSpec("cubic-bezier(.2,0,0,1)")
    val vumaTickDurationMs = 220
    val vumaTickCurve = androidx.compose.animation.core.CurveSpec("cubic-bezier(.65,0,.35,1)")
}

object VumaTouchTargetTokens {
    val posPrimarySize = 64.dp
    val posSecondarySize = 48.dp
    val backOfficeSize = 36.dp
    val androidWarehouseSize = 56.dp
}
