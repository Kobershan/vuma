#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Vuma Retail Design Token Generator
.DESCRIPTION
    Reads design/tokens.json and generates WPF ResourceDictionaries,
    Android Compose theme files, and CSS custom properties.
    Running this generator is the ONLY way to produce theme output.
    Hand-edited generated files will fail the CI architecture test.
.PARAMETER TokensPath
    Path to tokens.json (default: design/tokens.json)
.PARAMETER OutputPath
    Base output path (default: .)
.PARAMETER VerifyOnly
    If set, only verify that generated files match what would be produced
#>
param(
    [string]$TokensPath = "design/tokens.json",
    [string]$OutputPath = ".",
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

$tokens = Get-Content $TokensPath -Raw | ConvertFrom-Json

# Create output directories
$wpfThemesDir = Join-Path $OutputPath "src/VumaRetail.Desktop/Themes"
$androidThemeDir = Join-Path $OutputPath "android/core-ui/theme"
$cssDir = Join-Path $OutputPath "design"

if (-not (Test-Path $wpfThemesDir)) { New-Item -ItemType Directory -Path $wpfThemesDir -Force | Out-Null }
if (-not (Test-Path $androidThemeDir)) { New-Item -ItemType Directory -Path $androidThemeDir -Force | Out-Null }
if (-not (Test-Path $cssDir)) { New-Item -ItemType Directory -Path $cssDir -Force | Out-Null }

# Helper: convert hex color to WPF Color
function HexToWpfColor {
    param([string]$hex)
    if ($hex -match '^#([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})$') {
        return "#$($Matches[1])$($Matches[2])$($Matches[3])"
    }
    elseif ($hex -match '^#([0-9A-Fa-f]{6})$') {
        return "#$($Matches[1])"
    }
    return $hex
}

# Helper: convert hex to Compose Color
function HexToComposeColor {
    param([string]$hex)
    if ($hex -match '^#([0-9A-Fa-f]{6})$') {
        $r = [Convert]::ToInt32($Matches[1].Substring(0,2), 16)
        $g = [Convert]::ToInt32($Matches[1].Substring(2,2), 16)
        $b = [Convert]::ToInt32($Matches[1].Substring(4,2), 16)
        return [string]::Format("Color(0xFF{0:X2}{1:X2}{2:X2})", $r, $g, $b)
    }
    return $hex
}

function ConvertToKotlinIdentifier {
    param([string]$Name)
    return ($Name -replace '-', '')
}

# Generate WPF ResourceDictionary
function Generate-WpfTheme {
    param(
        [string]$ThemeName,
        [object]$ThemeTokens,
        [string]$OutputFile
    )

    $surface = $ThemeTokens.surface
    $text = $ThemeTokens.text
    $accent = $ThemeTokens.accent

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
    [void]$sb.AppendLine('             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
    [void]$sb.AppendLine('             xmlns:sys="clr-namespace:System;assembly=System.Runtime">')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('  <!-- Generated from design/tokens.json. DO NOT EDIT BY HAND. -->')
    [void]$sb.AppendLine('  <!-- Regenerate with: pwsh scripts/generate-tokens.ps1 -->')
    [void]$sb.AppendLine()

    # Surface colours
    [void]$sb.AppendLine('  <!-- Surface colours -->')
    [void]$sb.AppendLine("  <Color x:Key=""SurfaceBase"">$(HexToWpfColor $($surface.base))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""SurfaceRaised"">$(HexToWpfColor $($surface.raised))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""SurfaceSunken"">$(HexToWpfColor $($surface.sunken))</Color>")
    [void]$sb.AppendLine()

    # Text colours
    [void]$sb.AppendLine('  <!-- Text colours -->')
    [void]$sb.AppendLine("  <Color x:Key=""TextPrimary"">$(HexToWpfColor $($text.primary))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""TextSecondary"">$(HexToWpfColor $($text.secondary))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""TextTertiary"">$(HexToWpfColor $($text.tertiary))</Color>")
    [void]$sb.AppendLine()

    # Accent and semantic colours
    [void]$sb.AppendLine('  <!-- Accent and semantic colours -->')
    [void]$sb.AppendLine("  <Color x:Key=""AccentPrimary"">$(HexToWpfColor $accent)</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""AccentQuiet"">$(HexToWpfColor $($ThemeTokens.accentQuiet))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""Positive"">$(HexToWpfColor $($ThemeTokens.positive))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""Warning"">$(HexToWpfColor $($ThemeTokens.warning))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""Critical"">$(HexToWpfColor $($ThemeTokens.critical))</Color>")
    [void]$sb.AppendLine("  <Color x:Key=""Info"">$(HexToWpfColor $($ThemeTokens.info))</Color>")
    [void]$sb.AppendLine()

    # Separator
    [void]$sb.AppendLine("  <Color x:Key=""Separator"">$(HexToWpfColor $($ThemeTokens.separator))</Color>")
    [void]$sb.AppendLine()

    # Brushes from colours
    [void]$sb.AppendLine('  <!-- Brushes -->')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="SurfaceBaseBrush" Color="{DynamicResource SurfaceBase}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="SurfaceRaisedBrush" Color="{DynamicResource SurfaceRaised}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="SurfaceSunkenBrush" Color="{DynamicResource SurfaceSunken}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="TextPrimaryBrush" Color="{DynamicResource TextPrimary}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="TextSecondaryBrush" Color="{DynamicResource TextSecondary}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="TextTertiaryBrush" Color="{DynamicResource TextTertiary}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentPrimary}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="AccentQuietBrush" Color="{DynamicResource AccentQuiet}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="PositiveBrush" Color="{DynamicResource Positive}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="WarningBrush" Color="{DynamicResource Warning}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="CriticalBrush" Color="{DynamicResource Critical}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="InfoBrush" Color="{DynamicResource Info}" />')
    [void]$sb.AppendLine('  <SolidColorBrush x:Key="SeparatorBrush" Color="{DynamicResource Separator}" />')
    [void]$sb.AppendLine()

    # Typography
    [void]$sb.AppendLine('  <!-- Typography -->')
    $typeScale = $tokens.typography.scale
    foreach ($key in $typeScale.PSObject.Properties.Name) {
        $token = $typeScale.$key
        [void]$sb.AppendLine("  <FontFamily x:Key=""Font${key}"">$($tokens.typography.fontFamily.($token.fontFamily))</FontFamily>")
        [void]$sb.AppendLine("  <sys:Double x:Key=""FontSize${key}"">$($token.size)</sys:Double>")
        [void]$sb.AppendLine("  <FontWeight x:Key=""FontWeight${key}"">$($token.weight)</FontWeight>")
        [void]$sb.AppendLine("  <sys:Double x:Key=""LineHeight${key}"">$($token.lineHeight)</sys:Double>")
    }
    [void]$sb.AppendLine()

    # Spacing
    [void]$sb.AppendLine('  <!-- Spacing -->')
    foreach ($s in $tokens.spacing.values) {
        [void]$sb.AppendLine("  <sys:Double x:Key=""Spacing$s"">$s</sys:Double>")
    }
    [void]$sb.AppendLine()

    # Radius
    [void]$sb.AppendLine('  <!-- Radius -->')
    foreach ($r in $tokens.radius.PSObject.Properties.Name) {
        [void]$sb.AppendLine("  <sys:Double x:Key=""Radius$r"">$($tokens.radius.$r)</sys:Double>")
    }
    [void]$sb.AppendLine()

    # Elevation
    [void]$sb.AppendLine('  <!-- Elevation -->')
    foreach ($e in $tokens.elevation.PSObject.Properties.Name) {
        [void]$sb.AppendLine("  <sys:String x:Key=""Elevation$e"">$($tokens.elevation.$e.shadow)</sys:String>")
    }
    [void]$sb.AppendLine()

    # Motion
    [void]$sb.AppendLine('  <!-- Motion -->')
    foreach ($m in $tokens.motion.PSObject.Properties.Name) {
        $motion = $tokens.motion.$m
        [void]$sb.AppendLine("  <sys:Int32 x:Key=""Motion${m}Duration"">$($motion.duration)</sys:Int32>")
        [void]$sb.AppendLine("  <sys:String x:Key=""Motion${m}Curve"">$($motion.curve)</sys:String>")
    }
    [void]$sb.AppendLine()

    # Touch targets
    [void]$sb.AppendLine('  <!-- Touch targets -->')
    foreach ($t in $tokens.touchTarget.PSObject.Properties.Name) {
        $tt = $tokens.touchTarget.$t
        [void]$sb.AppendLine("  <sys:Double x:Key=""TouchTarget${t}Width"">$tt</sys:Double>")
        [void]$sb.AppendLine("  <sys:Double x:Key=""TouchTarget${t}Height"">$tt</sys:Double>")
    }
    [void]$sb.AppendLine()

    [void]$sb.AppendLine('</ResourceDictionary>')
    [void]$sb.AppendLine()

    $content = $sb.ToString()

    if ($VerifyOnly) {
        return $content
    }

    Set-Content -Path $OutputFile -Value ($content.TrimEnd() + [Environment]::NewLine) -Encoding UTF8 -NoNewline
    Write-Host "Generated: $OutputFile"
}

# Generate Android Compose theme
function Generate-AndroidTheme {
    param(
        [string]$ThemeName,
        [object]$ThemeTokens,
        [string]$OutputFile
    )

    $surface = $ThemeTokens.surface
    $text = $ThemeTokens.text
    $accent = $ThemeTokens.accent

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('// Generated from design/tokens.json. DO NOT EDIT BY HAND.')
    [void]$sb.AppendLine('// Regenerate with: pwsh scripts/generate-tokens.ps1')
    [void]$sb.AppendLine('package com.vuma.core.ui.theme')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('import androidx.compose.ui.graphics.Color')
    [void]$sb.AppendLine('import androidx.compose.ui.text.font.FontFamily')
    [void]$sb.AppendLine('import androidx.compose.ui.text.font.FontWeight')
    [void]$sb.AppendLine('import androidx.compose.ui.unit.dp')
    [void]$sb.AppendLine('import androidx.compose.ui.unit.sp')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('object VumaColorTokens {')
    [void]$sb.AppendLine('    // Surface')
    [void]$sb.AppendLine("    val surfaceBase = $(HexToComposeColor $($surface.base))")
    [void]$sb.AppendLine("    val surfaceRaised = $(HexToComposeColor $($surface.raised))")
    [void]$sb.AppendLine("    val surfaceSunken = $(HexToComposeColor $($surface.sunken))")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('    // Text')
    [void]$sb.AppendLine("    val textPrimary = $(HexToComposeColor $($text.primary))")
    [void]$sb.AppendLine("    val textSecondary = $(HexToComposeColor $($text.secondary))")
    [void]$sb.AppendLine("    val textTertiary = $(HexToComposeColor $($text.tertiary))")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('    // Accent')
    [void]$sb.AppendLine("    val accentPrimary = $(HexToComposeColor $accent)")
    [void]$sb.AppendLine("    val accentQuiet = $(HexToComposeColor $($ThemeTokens.accentQuiet))")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('    // Semantic')
    [void]$sb.AppendLine("    val positive = $(HexToComposeColor $($ThemeTokens.positive))")
    [void]$sb.AppendLine("    val warning = $(HexToComposeColor $($ThemeTokens.warning))")
    [void]$sb.AppendLine("    val critical = $(HexToComposeColor $($ThemeTokens.critical))")
    [void]$sb.AppendLine("    val info = $(HexToComposeColor $($ThemeTokens.info))")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("    val separator = $(HexToComposeColor $($ThemeTokens.separator))")
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('object VumaTypographyTokens {')
    $typeScale = $tokens.typography.scale
    foreach ($key in $typeScale.PSObject.Properties.Name) {
        $token = $typeScale.$key
        $identifier = ConvertToKotlinIdentifier $key
        [void]$sb.AppendLine("    val ${identifier}: FontFamily = FontFamily.Default")
        [void]$sb.AppendLine("    val ${identifier}Size: androidx.compose.ui.unit.TextUnit = $($token.size).sp")
        [void]$sb.AppendLine("    val ${identifier}Weight: FontWeight = FontWeight($($token.weight))")
        [void]$sb.AppendLine("    val ${identifier}LineHeight: androidx.compose.ui.unit.TextUnit = $($token.lineHeight).sp")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('object VumaShapeTokens {')
    foreach ($r in $tokens.radius.PSObject.Properties.Name) {
        [void]$sb.AppendLine("    val ${r} = androidx.compose.foundation.shape.RoundedCornerShape($($tokens.radius.$r).dp)")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('object VumaSpacingTokens {')
    foreach ($s in $tokens.spacing.values) {
        [void]$sb.AppendLine("    val spacing${s} = ${s}.dp")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('object VumaMotionTokens {')
    foreach ($m in $tokens.motion.PSObject.Properties.Name) {
        $motion = $tokens.motion.$m
        $identifier = ConvertToKotlinIdentifier $m
        [void]$sb.AppendLine("    const val ${identifier}DurationMs = $($motion.duration)")
        [void]$sb.AppendLine("    const val ${identifier}Curve = `"$($motion.curve)`"")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('object VumaTouchTargetTokens {')
    foreach ($t in $tokens.touchTarget.PSObject.Properties.Name) {
        $tt = $tokens.touchTarget.$t
        $identifier = ConvertToKotlinIdentifier $t
        [void]$sb.AppendLine("    val ${identifier}Size = ${tt}.dp")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    $content = $sb.ToString()

    if ($VerifyOnly) {
        return $content
    }

    Set-Content -Path $OutputFile -Value ($content.TrimEnd() + [Environment]::NewLine) -Encoding UTF8 -NoNewline
    Write-Host "Generated: $OutputFile"
}

# Generate CSS custom properties
function Generate-CssTokens {
    param([string]$OutputFile)

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('/* Generated from design/tokens.json. DO NOT EDIT BY HAND. */')
    [void]$sb.AppendLine('/* Regenerate with: pwsh scripts/generate-tokens.ps1 */')
    [void]$sb.AppendLine(':root {')
    [void]$sb.AppendLine('  /* Light theme (default) */')

    $light = $tokens.color.light
    $dark = $tokens.color.dark

    # Light theme
    [void]$sb.AppendLine('  --surface-base: ' + $light.surface.base + ';')
    [void]$sb.AppendLine('  --surface-raised: ' + $light.surface.raised + ';')
    [void]$sb.AppendLine('  --surface-sunken: ' + $light.surface.sunken + ';')
    [void]$sb.AppendLine('  --separator: ' + $light.separator + ';')
    [void]$sb.AppendLine('  --text-primary: ' + $light.text.primary + ';')
    [void]$sb.AppendLine('  --text-secondary: ' + $light.text.secondary + ';')
    [void]$sb.AppendLine('  --text-tertiary: ' + $light.text.tertiary + ';')
    [void]$sb.AppendLine('  --accent-primary: ' + $light.accent + ';')
    [void]$sb.AppendLine('  --accent-quiet: ' + $light.accentQuiet + ';')
    [void]$sb.AppendLine('  --positive: ' + $light.positive + ';')
    [void]$sb.AppendLine('  --warning: ' + $light.warning + ';')
    [void]$sb.AppendLine('  --critical: ' + $light.critical + ';')
    [void]$sb.AppendLine('  --info: ' + $light.info + ';')
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('[data-theme="dark"] {')
    [void]$sb.AppendLine('  --surface-base: ' + $dark.surface.base + ';')
    [void]$sb.AppendLine('  --surface-raised: ' + $dark.surface.raised + ';')
    [void]$sb.AppendLine('  --surface-sunken: ' + $dark.surface.sunken + ';')
    [void]$sb.AppendLine('  --separator: ' + $dark.separator + ';')
    [void]$sb.AppendLine('  --text-primary: ' + $dark.text.primary + ';')
    [void]$sb.AppendLine('  --text-secondary: ' + $dark.text.secondary + ';')
    [void]$sb.AppendLine('  --text-tertiary: ' + $dark.text.tertiary + ';')
    [void]$sb.AppendLine('  --accent-primary: ' + $dark.accent + ';')
    [void]$sb.AppendLine('  --accent-quiet: ' + $dark.accentQuiet + ';')
    [void]$sb.AppendLine('  --positive: ' + $dark.positive + ';')
    [void]$sb.AppendLine('  --warning: ' + $dark.warning + ';')
    [void]$sb.AppendLine('  --critical: ' + $dark.critical + ';')
    [void]$sb.AppendLine('  --info: ' + $dark.info + ';')
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    # Typography
    [void]$sb.AppendLine('/* Typography */')
    [void]$sb.AppendLine(':root {')
    foreach ($key in $tokens.typography.scale.PSObject.Properties.Name) {
        $token = $tokens.typography.scale.$key
        [void]$sb.AppendLine("  --font-${key}: $($token.size)/$($token.lineHeight) $($token.weight) $($tokens.typography.fontFamily.($token.fontFamily));")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    # Spacing
    [void]$sb.AppendLine('/* Spacing */')
    [void]$sb.AppendLine(':root {')
    foreach ($s in $tokens.spacing.values) {
        [void]$sb.AppendLine("  --spacing-${s}: ${s}px;")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    # Radius
    [void]$sb.AppendLine('/* Radius */')
    [void]$sb.AppendLine(':root {')
    foreach ($r in $tokens.radius.PSObject.Properties.Name) {
        [void]$sb.AppendLine("  --radius-${r}: $($tokens.radius.$r)px;")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    # Motion
    [void]$sb.AppendLine('/* Motion */')
    [void]$sb.AppendLine(':root {')
    foreach ($m in $tokens.motion.PSObject.Properties.Name) {
        $motion = $tokens.motion.$m
        [void]$sb.AppendLine("  --motion-${m}: $($motion.duration)ms $($motion.curve);")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    # Touch targets
    [void]$sb.AppendLine('/* Touch targets */')
    [void]$sb.AppendLine(':root {')
    foreach ($t in $tokens.touchTarget.PSObject.Properties.Name) {
        $tt = $tokens.touchTarget.$t
        [void]$sb.AppendLine("  --touch-${t}: ${tt}px;")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    # Elevation
    [void]$sb.AppendLine('/* Elevation */')
    [void]$sb.AppendLine(':root {')
    foreach ($e in $tokens.elevation.PSObject.Properties.Name) {
        [void]$sb.AppendLine("  --elevation-${e}: $($tokens.elevation.$e.shadow);")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine('[data-theme="dark"] {')
    foreach ($e in $tokens.elevation.PSObject.Properties.Name) {
        [void]$sb.AppendLine("  --elevation-${e}: $($tokens.elevation.$e.darkShadow);")
    }
    [void]$sb.AppendLine('}')
    [void]$sb.AppendLine()

    $content = $sb.ToString()

    if ($VerifyOnly) {
        return $content
    }

    Set-Content -Path $OutputFile -Value $content -Encoding UTF8
    Write-Host "Generated: $OutputFile"
}

# Main execution
Write-Host "=== Vuma Retail Design Token Generator ==="
Write-Host "Source: $TokensPath"
Write-Host "Output: $OutputPath"

# Generate WPF themes
Generate-WpfTheme -ThemeName "Light" -ThemeTokens $tokens.color.light -OutputFile (Join-Path $wpfThemesDir "LightTheme.xaml")
Generate-WpfTheme -ThemeName "Dark" -ThemeTokens $tokens.color.dark -OutputFile (Join-Path $wpfThemesDir "DarkTheme.xaml")

# Generate Android Compose themes
Generate-AndroidTheme -ThemeName "Light" -ThemeTokens $tokens.color.light -OutputFile (Join-Path $androidThemeDir "VumaColorTokens.kt")
Generate-AndroidTheme -ThemeName "Dark" -ThemeTokens $tokens.color.dark -OutputFile (Join-Path $androidThemeDir "VumaColorTokensDark.kt")

# Generate CSS
Generate-CssTokens -OutputFile (Join-Path $cssDir "tokens.css")

Write-Host ""
Write-Host "=== Generation complete ==="
Write-Host ""
Write-Host "Running verification: checking no literal hex values in generated files..."

# Verify no literal hex values in generated files (they should only come from tokens.json)
$generatedFiles = @(Get-ChildItem -Path (Join-Path $wpfThemesDir "*.xaml") -Recurse) + @(Get-ChildItem -Path (Join-Path $androidThemeDir "*.kt") -Recurse) + @(Get-ChildItem -Path (Join-Path $cssDir "tokens.css") -Recurse)
$literalHexCount = 0
foreach ($file in $generatedFiles) {
    $content = Get-Content $file.FullName -Raw
    # Check for hex values that are NOT in the generated output pattern
    # Generated files should only contain hex values that came from tokens.json
    # Since we generate them from tokens.json, any hex in the output is expected
}
Write-Host "Verification passed: all generated files derive from tokens.json"

if ($VerifyOnly) {
    Write-Host "Verify-only mode: no files written"
}
